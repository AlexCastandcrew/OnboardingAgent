# HTGPlus.ExcelMapping — Target Redesign (Concern-Separated Pods)

Source: Confluence page 5412749317, space HPS, last modified sept 01, 2026.

## Status and Purpose

IN PROGRESS   **Last Updated:** 2026-09-01

This document describes the target redesign of HTGPlus.ExcelMapping into pods separated by concern. It is intended for the next developer picking up this work.

For background on why the current multiconsumer architecture is wrong for ExcelMapping, see [HTGPlus.ExcelMapping.Subscriber — Architecture Design](https://castandcrew.atlassian.net/wiki/spaces/HPS/pages/5385224223/HTGPlus.ExcelMapping.Subscriber+Architecture+Design).

## Design Principle: One Pod Per Concern

The current architecture mixes multiple concerns in one pod. Each concern has different scaling needs, failure modes, and latency characteristics. Separating them gives independent scalability, independent failure isolation, and a simpler mental model for each service.

| Pod | Concern | Input | Output | Replicas |
| --- | --- | --- | --- | --- |
| `sqs-subscriber` | Forward SQS events to Kafka | AWS SQS | `{env}-htg-studio-import` | Fixed: 1 |
| `template-decider` | Download file, resolve template, route by ExecutionType | `{env}-htg-studio-import` | `{env}-htg-traditional-import`, `{env}-htg-docai-import`, `{env}-htg-unstructured-import` or `AicoFileImported` (Unstructured, depending on the cutover flag) | Scalable |
| `extract-processor` | Download file, run full extraction + transform per type (Traditional and DocAI only); publishes a finished DataExtractionResponse — mapping-processor receives it ready to map | `{env}-htg-traditional-import`, `{env}-htg-docai-import` | `{env}-htg-traditional-extract`, `{env}-htg-docai-extract` | Fixed: 1 (deployed) |
| `mapping-processor` | Map extracted data to timecards, publish to CAPS — a new pod, dispatcher-pattern consumer | `{env}-htg-traditional-extract`, `aicoe-htg-unstructured-extract`, `{env}-htg-docai-extract` | `HtgCapsTimecardExported`, `AicoCapsTimecardExported` | Scalable |

## Cutover Model: One Whole-System Flag, Not a Partial Migration

The existing pipeline (today's `kafka-processor` deployment, `htg-excelmapping-subscriber`) is **never modified** as part of this redesign — no responsibilities are removed from it, no code inside it changes. It keeps doing Traditional, DocAI, and Unstructured processing exactly as it does today, for as long as the "use new excel import pipeline" flag is off.

The four new pods (`sqs-subscriber`, `template-decider`, `extract-processor`, `mapping-processor`) are a fully independent, parallel implementation of the same job, built from scratch, running alongside the old pipeline during validation. There is no partial cutover — the flag is a single, whole-system switch:

- **Flag off**: old pipeline is authoritative for all three types (Traditional, DocAI, Unstructured). New pods run and can be validated, but nothing from them is treated as the source of truth yet.
- **Flag on**: old pipeline is fully deactivated. The four new pods handle all three types end to end.

This is deliberate: since mapping-processor's job ends in a real side effect (publishing to CAPS), running old and new *simultaneously as both authoritative* for the same file would create duplicate timecards. The flag ensures only one system is ever authoritative at a time, for all types at once — never a mix where, say, Traditional is cut over but DocAI is not.

**Revert**: flip the flag back off. Because the old pipeline was never touched, it is instantly correct and authoritative again — no rollback, no redeploy, no code to restore. This does not undo anything already processed incorrectly by the new pipeline while it was live; that needs separate remediation regardless of which way the flag moves.

**Already built and live in code**: the flag is `ExcelMappingFeatureFlags.UseNewExcelImportPipeline`, checked via `IUnleash.IsEnabled(...)` in `TemplateDeciderRouteProvider`. Today it controls three things specifically: (1) whether template-decider updates Studio+ file status to `Processing` when it begins routing a file, (2) which topic Unstructured messages are produced to (see template-decider section below), and (3) whether template-decider updates Studio+ file status to `Submitted` when no template matches any sheet — it is not yet wired into extract-processor or mapping-processor, since neither is authoritative in production yet.

## Error Notification: Dashboard Replaces Tech-Team Email

The old pipeline's `BaseProcessProvider.SendExtractionFailureNotificationAsync` emails the tech team (`ExcelmappingTechTeamEmail`) whenever a Kafka publish fails or a file ends in `Error` status — a workaround for not having real-time visibility into failures any other way. Neither template-decider nor extract-processor sends an equivalent email; both rely entirely on the File Tracking Dashboard (via `IFileTrackerProvider`) for failure visibility instead, plus a SignalR notification to the uploader (not the tech team) for non-StudioPlus origins.

This is a deliberate decision, not a gap: once the dashboard is the primary visibility tool (see H2G-6620/H2G-6621), tech-team email alerting isn't carried forward to the new pipeline. Whoever owns the new pipeline's failures is expected to watch the dashboard, not an inbox.

## Pod Responsibilities

### 1. sqs-subscriber

Polls AWS SQS, deserializes the SNS envelope, produces a `FileImportedMessage` to `{env}-htg-studio-import`, calls `IFileTrackerProvider.TrackQueuedFile()`, and deletes the SQS message. Fixed at 1 replica to avoid duplicate Kafka events from competing SQS consumers. No file processing happens here.

### 2. template-decider

Consumes from `{env}-htg-studio-import`. Live in production today. For each message:

1. Calls `ProcessFileRequestFactory.CreateFromStudioPlusAsync` — for StudioPlus-origin messages, this makes 3 CapsPay calls before the file is downloaded: `v2/getEorIdByClientId` (resolve EOR from clientId), `getClientDetails` (resolve parent client), and `getClientDetailsByClientId` (resolve client type). EOR is not present in the incoming StudioPlus message — it is looked up here.
2. Downloads the file from StudioPlus, then resolves worksheets (from metadata, or via `IExtractionService.GetWorksheetsAsync` if not already present).
3. If the `UseNewExcelImportPipeline` flag is on, updates Studio+ file status to `Processing`.
4. For each worksheet, resolves `AssignImportTemplate` → `ImportTemplate` to determine `ExecutionType`:
    - No template matched, or template excluded: logs a descriptive message (distinguishing the two cases), calls `TrackErrorSheet` so it shows up on the File Tracking Dashboard, and — only when the file's origin is not StudioPlus — sends a SignalR notification to the uploader. No message is produced for this worksheet.
    - Traditional or DocAI: produces the **unchanged, original** `FileImportedMessage` to `{env}-htg-traditional-import` or `{env}-htg-docai-import`, deduplicated so only one message goes out per topic per file, regardless of how many worksheets resolve to it. Worksheets that resolve to an already-routed topic are skipped (no Kafka message produced) but still receive a `DashboardSheetTracking:Routed` entry via `TrackRoutedSheet` — they appear as Routed on the File Tracking Dashboard, not as skipped or missing.
    - Unstructured: not produced immediately. The worksheet's `Workflow` value is recorded (the *last* Unstructured worksheet in the file wins if there's more than one, mirroring the old pipeline's behavior), and after all worksheets are processed, one `AICOEImportedMessage` is produced — carrying `MessageId`, `Body`, the winning `Workflow`, plus `FileKey`, `FileName`, `FileUploadedBy`, `Client`, and `PackageTrackingId` (added specifically so the message is searchable in Lenses, since the old AiCOE message shape only had `MessageId` as a queryable field). This message goes to `{env}-htg-unstructured-import` (the interim topic) when the flag is off, or directly to the existing `AicoFileImported`/`htg-aicoe-studio-import` topic AiCOE already consumes when the flag is on.
5. If nothing was routed for the file and the flag is on and the origin is not StudioPlus, reverts Studio+ file status to `Submitted`.
6. Any unexpected exception routing a worksheet is caught per-worksheet (one bad worksheet doesn't abort the others), tracked via `TrackErrorSheet`, and logged.
7. Commits offset and moves on — no extraction happens here.

**Correction: the template decision is not carried forward in the message payload for Traditional or DocAI.** An earlier version of this doc claimed it was, so downstream pods wouldn't need to re-resolve the template — that was aspirational, not actual. The message produced for Traditional/DocAI is the original, unchanged `FileImportedMessage`, with no `ExecutionType` or template info attached. This is consistent with the extract-processor section below, which explicitly re-resolves the template itself — the two sections used to contradict each other on this point. Whether to actually carry the decision forward is still an open item (see below).

**Not yet implemented**: retry-on-failure for a transient file download, template lookup, or Kafka produce error — tracked separately in H2G-6523.

**Known inefficiency — duplicate CapsPay calls per file:** `ProcessFileRequestFactory.CreateFromStudioPlusAsync` runs independently in `template-decider`, `kafka-processor` (old pipeline), and `TraditionalExtractHandler` (extract-processor). Each consumer makes the same 3 CapsPay calls, so every StudioPlus file triggers 6 CapsPay calls total while both pipelines are live.

### 3. extract-processor

**Status: merged to** `development` (verified 2026-08-15), not yet on `master`/`release` — not authoritative in production. Deployed via `RUN_MODE=extract-processor` in the same `HTGPlus.ExcelMapping` binary as the other pods, `replicas: 1`.

Built as **one shared engine, not two independent implementations** — an earlier version of this doc drew Traditional and DocAI as two parallel, near-duplicate consumer chains. The actual code is a single `ExtractProvider` class that owns the entire per-worksheet pipeline (file download, worksheet resolution, template re-resolution, dashboard tracking, calling the lane's extraction logic, publishing, error handling, and uploader notification), parameterized by an `IExtractionStrategy`. `TraditionalExtractionStrategy` and `DocAIExtractionStrategy` supply only the lane-specific extraction call plus `LaneType`/`TopicAlias` — everything else is shared, so a fix to the shared pipeline (e.g. the per-worksheet loop or error handling) only needs to happen once, not twice. `TraditionalExtractProvider`/`DocAIExtractProvider` are thin wrappers delegating to that shared engine, and `TraditionalExtractHandler`/`DocAIExtractHandler` are the Kafka message handlers registered per lane.

```
extract-processor pod
├── TraditionalExtractHandler (topic: {env}-htg-traditional-import)
│     └─► TraditionalExtractProvider ──┐
│                                       │
└── DocAIExtractHandler (topic: {env}-htg-docai-import)
      └─► DocAIExtractProvider ─────────┤
                                         ▼
                            ExtractProvider (shared engine, one per lane instance)
                              0. Calls ProcessFileRequestFactory.CreateRequestAsync(message.Body) —
                                 for StudioPlus-origin messages, makes the same 3 CapsPay calls as
                                 template-decider before any file download: v2/getEorIdByClientId
                                 (EOR), getClientDetails (parent client), getClientDetailsByClientId
                                 (client type). This is the 3rd independent CapsPay call per file
                                 while both pipelines run in parallel — see Known inefficiency above.
                              1. Download file from StudioPlus (2nd download)
                              2. Resolve worksheets
                              3. Per worksheet:
                                   - re-resolve template
                                   - guard: template still matched/non-excluded, AND still
                                     resolves to this handler's lane — a mismatch means the
                                     rule table changed between template-decider's routing and
                                     this extraction; tracks Error, notifies non-StudioPlus
                                     uploaders, skips just that worksheet (doesn't abort the file)
                                   - calls TraditionalExtractionStrategy.RunAsync() or
                                     DocAIExtractionStrategy.RunAsync() (the only lane-specific step)
                                   - publishes one FileExtractedMessage per extracted item to
                                     {env}-htg-traditional-extract or {env}-htg-docai-extract
                                     (a single worksheet can yield multiple messages if it contains
                                     multiple extracted items)
```

Registered via `UseMulticonsumer(...).AddDedicatedJsonHandler<TraditionalExtractHandler, FileImportedMessage>(...).AddDedicatedJsonHandler<DocAIExtractHandler, FileImportedMessage>(...)`. Each `AddDedicatedJsonHandler` call registers its own independent `DedicatedJsonConsumer` hosted service with its own poll loop, bypassing the shared multi-topic consumer entirely — so this is still the two-fully-independent-consumers architecture the redesign calls for (a crash/restart of one lane doesn't affect the other's offset or processing), just built on the `MultiConsumerBuilder`'s dedicated-handler registration path rather than instantiating `DedicatedTopicConsumer<T>` directly, which an earlier version of this doc assumed.

Full transform ownership: extract-processor runs the complete transform for both Traditional and DocAI before publishing — extraction, metadata enrichment, and any post-extraction derivations. By the time `FileExtractedMessage` reaches mapping-processor, its item is a finished `DataExtractionResponse` with all enriched fields already set (including `DirectDepositDate`/`PayrollFrequency` for DocAI). Mapping-processor does not re-derive or re-transform anything for these two lanes; it only re-resolves the template for the `MapAsync` call itself.

**Dashboard tracking today**: `TrackErrorFile` for file-level failures (request parse, download, no worksheets found), `TrackInProgressSheet`/`TrackErrorSheet` per worksheet. There is no success/close-out tracking call yet for a worksheet that extracts successfully — this is the gap H2G-6620's new `Extracted` status is meant to fill.

**DocAI going away:** Business will disable and eventually remove all DocAI-type templates. Once that happens, the DocAI lane will be idle and can be removed — the shared `ExtractProvider` engine and the Traditional lane are unaffected.

**Known production issue, root cause not yet diagnosed:** in production today, scaling the current (old-pipeline) DocAI processing to 3 instances causes one pod to stop processing; reducing to 2 instances resolves it. There is no confirmed evidence that the DocAI API call itself hangs — that is one possible explanation, not a proven one. This is the kind of issue extract-processor's dedicated-per-lane isolation is intended to prevent going forward.

### 4. mapping-processor

**Status: merged to `development`, not yet on `master`/`release` — not authoritative in production. Deployed via `RUN_MODE=mapping-processor` in the same `HTGPlus.ExcelMapping` binary as the other pods.**

**A new pod** — not a modification of the old pipeline. Built as one dedicated pattern consumer, the same shape as `PayrollResponseConsumer` in `HTGPlus.LookupEntityProcessor`: a single `BackgroundService`, its own consumer group, subscribed to a topic pattern covering all three extract-result topics, dispatching each message to the right mapping logic by topic. Adding a topic later means registering a new handler, not standing up a new consumer.

```
mapping-processor (new pod, dispatcher pattern, like PayrollResponseConsumer)
├── Subscribes to: {env}-htg-traditional-extract, aicoe-htg-unstructured-extract, {env}-htg-docai-extract
├── One dedicated consumer group, one background loop
└── Dispatch by topic:
      ├── traditional-extract  → re-resolve template for MapAsync
      │                          → IIngestionMapping.MapAsync(..., ExecutionType.Traditional)
      ├── unstructured-extract → re-resolve template + run transform (old FileExtractedHandler logic:
      │                          maps raw AiCOE response → DataExtractionResponse, since Unstructured
      │                          never passes through extract-processor)
      │                          → IIngestionMapping.MapAsync(..., ExecutionType.Unstructured)
      └── docai-extract        → re-resolve template for MapAsync (arrives pre-transformed from
                                   extract-processor's already-built DocAIExtractHandler/DocAIExtractProvider,
                                   same shape as the Traditional branch)
                                   → IIngestionMapping.MapAsync(..., ExecutionType.AI)
```

All three branches call the same `IIngestionMapping.MapAsync()` pipeline. The `ExecutionType` key selects the correct `IDeductionDistributionMapping` implementation — no branching in the shared mapping code.

**Resolved, no longer an open concern:** sequential, single-thread processing across all three topics is a deliberate, confirmed-good choice, not a starvation risk. None of the three branches call a slow external API — all are local template-resolution plus in-process `IIngestionMapping.MapAsync()` work. Building a dedicated thread to protect DocAI's low volume (~1.2% of weekly files) from theoretical contention would be unwarranted engineering. This is different from extract-processor, which correctly keeps two separate dedicated consumers, because DocAI's *extraction* call there specifically depends on a slow external API — the risk that matters is duration-of-block on an external dependency, not message volume.

## Data Flow

```
StudioPlus (SNS → SQS)
  └─► sqs-subscriber
        └─► {env}-htg-studio-import

{env}-htg-studio-import
  └─► template-decider  [downloads file — 1st download]
        ├─► Traditional  → {env}-htg-traditional-import
        ├─► DocAI        → {env}-htg-docai-import
        └─► Unstructured → {env}-htg-unstructured-import (flag off) or AicoFileImported (flag on)  ← consumed by external AiCOE team

{env}-htg-traditional-import  →  extract-processor [downloads file — 2nd download]  →  {env}-htg-traditional-extract
{env}-htg-docai-import        →  extract-processor [downloads file — 2nd download]  →  {env}-htg-docai-extract
{env}-htg-unstructured-import →  [AiCOE external team]                              →  aicoe-htg-unstructured-extract (existing topic, unchanged name)

{env}-htg-traditional-extract       ──┐
{env}-htg-docai-extract             ──┤──► mapping-processor (new pod, dispatcher pattern — one consumer, 3 topics)
aicoe-htg-unstructured-extract      ──┘        ├─► HtgCapsTimecardExported   (Traditional + DocAI)
                                                └─► AicoCapsTimecardExported  (Unstructured)

(Old pipeline, htg-excelmapping-subscriber / kafka-processor: fully untouched, does all three types
inline, authoritative while the "use new excel import pipeline" flag is off. Not shown above — it does
not participate in the new topics at all.)
```

## Kafka Topic Reference

| Topic | Producer | Consumer | Notes |
| --- | --- | --- | --- |
| `{env}-htg-studio-import` | sqs-subscriber | template-decider | Existing topic |
| `{env}-htg-traditional-import` | template-decider | extract-processor | New topic |
| `{env}-htg-docai-import` | template-decider | extract-processor | New topic — will be retired when DocAI templates are removed |
| `{env}-htg-unstructured-import` | template-decider | AiCOE (external team) | New, interim topic — used only while the cutover flag is off; not consumed by AiCOE yet |
| `AicoFileImported` / `htg-aicoe-studio-import` | template-decider (flag on) or old pipeline (flag off) | AiCOE (external team) | Existing topic AiCOE already consumes today — template-decider switches to producing here once the cutover flag is on |
| `{env}-htg-traditional-extract` | extract-processor | mapping-processor | New topic |
| `{env}-htg-docai-extract` | extract-processor | mapping-processor | New topic — already built (PR #18724 / H2G-6453); DocAI's old inline path retires on full cutover, same as Traditional and Unstructured |
| `aicoe-htg-unstructured-extract` | AiCOE (external team) | mapping-processor | Existing topic, unchanged name — not renamed to `{env}-htg-unstructured-extract` |
| `HtgCapsTimecardExported` | mapping-processor | CAPS/DII | Traditional + DocAI results |
| `AicoCapsTimecardExported` | mapping-processor | CAPS/DII | Unstructured results |

## Implementation Notes for the Next Developer

- extract-processor's two lanes are registered via `UseMulticonsumer(...).AddDedicatedJsonHandler<THandler, TMessage>(...)` (one call per lane) — each call gets its own independent `DedicatedJsonConsumer` hosted service with its own poll loop, not the shared multi-topic consumer. mapping-processor uses a single dispatcher-pattern consumer instead (see `PayrollResponseConsumer` in `HTGPlus.LookupEntityProcessor` for the reference shape) — one `BackgroundService`, one consumer group, multiple topics dispatched by name.
- extract-processor's shared logic (download, worksheet resolution, template re-resolution + lane-mismatch guard, dashboard tracking, publish, error handling) lives in one `ExtractProvider` class parameterized by `IExtractionStrategy` — only the actual extraction call is lane-specific (`TraditionalExtractionStrategy`/`DocAIExtractionStrategy`). Add a new lane by implementing `IExtractionStrategy`, not by duplicating `ExtractProvider`.
- mapping-processor's rule cache (occupation codes, work location, pay type, deduction, rate expression, template assignment mappings) should be loaded once per pod instance and shared across all three dispatch branches — register it as a singleton.
- The template decision (ExecutionType) is **not currently** carried in the message payload — see the correction in the template-decider section above. For Traditional and DocAI, mapping-processor re-resolves the template only for the MapAsync call — the transform has already been done by extract-processor. For Unstructured, mapping-processor must re-resolve the template for both the transform and MapAsync, since Unstructured never passes through extract-processor.
- Both extract-processor lanes download the file from StudioPlus independently — `HTGPlus.StudioPlusIntegration` must be registered in the extract-processor DI container.
- The extract-processor's two lanes are independent — no shared state between them. A crash or restart of one does not affect the other's offset or processing.
- `EnableAutoOffsetStore = false` on all consumers — commit offset only after the downstream publish succeeds.
- Coordinate with the AiCOE external team on the message schema for `{env}-htg-unstructured-import` — their result schema and topic name (`aicoe-htg-unstructured-extract`) stay as they are today.
- The old pipeline (`htg-excelmapping-subscriber`, `RUN_MODE=kafka-processor`) is never modified as part of this work. Do not remove or refactor anything in it — see the Cutover Model section above.

## What Is Not Covered Here

* [ ] `HtgFileImportedSubscriber` — legacy HTG-internal Kafka subscriber is still commented out and not yet migrated.
* [ ] Rate expression coverage — unmapped expressions fall through silently with no warning.
* [ ] Batch date validation — no `ResolveBatchWeekEndingDate` fallback guard.
* [ ] File Tracking Dashboard needs to support the new granular statuses (`Routed`, `Extracted`) and the full end-state status progression once cutover completes — see H2G-6620 and H2G-6621.

### From the H2G-6391 (template-decider) review

* [ ] Still open: the message sent to any of the 3 typed topics doesn't say which worksheet triggered the routing decision (confirmed still true in shipped code, 2026-08-15). Whoever builds `extract-processor`'s consumption of this needs to re-resolve the template themselves unless this gets fixed first.
* [x] Resolved and shipped: multiple Unstructured worksheets now correctly use the *last* Workflow value, matching old pipeline behavior. Still a stopgap, not the real per-sheet-aware fix — that remains deferred.
* [ ] Whether all three routing topics could share one message shape (so only one Kafka producer is needed instead of two) is still an open idea, not a decision.
* [ ] Whether template-decider's Kubernetes deployment has a liveness probe configured has not been checked.
* [x] Resolved and shipped: the cutover flag (`ExcelMappingFeatureFlags.UseNewExcelImportPipeline` via Unleash) is built and live, gating Studio+ status updates and the Unstructured output topic. Not yet wired into extract-processor or mapping-processor.
* [ ] Whether to retire the DII batch record created today for "no template found" failures, in favor of the File Tracking Dashboard alone, is pending a decision from the business team.
* [ ] Retry-on-failure for template-decider (file download, template lookup, or Kafka produce failing temporarily) is tracked in H2G-6523, status Open as of 2026-08-14 — not yet implemented.

### From the extract-processor code review (2026-08-15)

* [x] Resolved and shipped: extract-processor exists and is merged to `development`, built as a shared `ExtractProvider` engine + `IExtractionStrategy` per lane, registered via `MultiConsumerBuilder.AddDedicatedJsonHandler` (two independent consumers). Not yet on `master`/`release`.
* [x] Resolved and shipped: a defensive guard re-validates each worksheet's template is still matched/non-excluded and still resolves to the lane it arrived on, protecting against the rule table changing between template-decider's routing and extract-processor's extraction.
* [x] Resolved (decision): tech-team email alerting on extraction/publish failure is not carried forward from the old pipeline — the File Tracking Dashboard replaces it. See the "Error Notification" section above.
* [ ] Extract-processor has no success/close-out tracking call for a worksheet that extracts without error — tracked as the `Extracted` status in H2G-6620.

### From the mapping-processor design discussion

* [x] Resolved: sequential, single-thread processing across all three extract topics is a confirmed-good, deliberate choice (see panel-note in the mapping-processor section above), not an open concern.
* [x] Resolved and shipped: mapping-processor exists and is merged to `development`, built as `MappingResponseConsumer` (dispatcher pattern) + `MultiTopicConsumer<byte[]>` subscribing to all three extract topics. `TraditionalMappingHandler`, `DocAIMappingHandler`, and `UnstructuredMappingHandler` are all implemented. Not yet on `master`/`release`.
* [ ] Whether the old pipeline needs a dry-run/non-publishing validation mode of its own, or whether the whole-system flag is sufficient on its own to prevent duplicate CAPS publishes during rollout.
