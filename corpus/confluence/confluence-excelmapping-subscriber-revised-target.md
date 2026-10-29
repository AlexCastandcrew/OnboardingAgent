# HTGPlus.ExcelMapping.Subscriber — Architecture Design (Revised — Target)

Source: Confluence page 5384536082, space HPS, last modified jul 18, 2026.

**Status:** Proposed (Target Architecture)   **Last Updated:** 2026-07-18

> **This is the revised target architecture.** It reflects changes that are in-progress or planned but not yet deployed:
> 
> - PR [#18571](https://github.com/cast-and-crew/htg-server/pull/18571) — sqs-subscriber / kafka-processor pod split (pending merge)
> - [H2G-6390](https://castandcrew.atlassian.net/browse/H2G-6390) / [H2G-6391](https://castandcrew.atlassian.net/browse/H2G-6391) / [H2G-6392](https://castandcrew.atlassian.net/browse/H2G-6392) / [H2G-6393](https://castandcrew.atlassian.net/browse/H2G-6393) — DocAI async isolation into dedicated pod
> 
> See the [current architecture document](https://castandcrew.atlassian.net/wiki/spaces/HPS/pages/5385224223/HTGPlus.ExcelMapping.Subscriber+Architecture+Design) for the deployed state. Once all tickets above are merged and deployed, this document supersedes it.

## Related Documents

- [ALES Timecard Ingestion — Excel File Flow](https://castandcrew.atlassian.net/wiki/spaces/HPS/pages/5199953928/ALES+Timecard+Ingestion+Excel+File+Flow) — end-to-end business flow: service responsibilities, Kafka topics, extraction type overview
- [HTGPlus.ExcelMapping.Subscriber — Architecture Design (Current)](https://castandcrew.atlassian.net/wiki/spaces/HPS/pages/5385224223/HTGPlus.ExcelMapping.Subscriber+Architecture+Design) — currently deployed architecture

## Purpose

HTGPlus.ExcelMapping.Subscriber bridges StudioPlus (an external payroll platform) and HTG's internal payroll system (CAPS/DII). It consumes file-import events from AWS SQS and Apache Kafka, downloads uploaded Excel/CSV files, maps their contents to a canonical payroll format using a rule-table-driven template engine, and publishes the mapped timecards back to Kafka for downstream consumers.

## Deployment Architecture

The service is deployed as **three independent pods** from the same Docker image, differentiated by the `RUN_MODE` environment variable.

| Pod | `RUN_MODE` value | Replicas | Role |
| --- | --- | --- | --- |
| `htg-excelmapping-sqs-subscriber` | `sqs-subscriber` | 1 (fixed) | Polls AWS SQS; relays file-import events to Kafka |
| `htg-excelmapping-kafka-processor` | `kafka-processor` (or unset) | Scalable | Routes by extraction type; processes Traditional inline; maps results from all async paths |
| `htg-excelmapping-docai-processor` | `docai-processor` | 1 (fixed, scalable if needed) | Makes the ~10-min DocAI REST call in isolation |

**Why three pods?**

The DocAI REST call is known to hang intermittently and takes up to 10 minutes when it succeeds. Before isolation, a hanging DocAI call blocked the same kafka-processor consumer thread that processes Traditional and Unstructured files. With a dedicated pod:

- A hang in the docai-processor triggers a Kubernetes liveness probe restart — the kafka-processor and sqs-subscriber pods are completely unaffected
- The kafka-processor consumer loop is never blocked by a long-running I/O call
- Each pod can be scaled, restarted, or rolled back independently

The sqs-subscriber is fixed at 1 replica because multiple replicas would race to consume and re-publish the same SQS messages, producing duplicate Kafka events. The kafka-processor scales based on consumer group lag.

## System Context Diagram

*End-to-end flow showing all three pods within the broader ALES ingestion pipeline. ★ marks changes from the current deployed architecture.*

[diagram omitted]

(updated version showing DocAI async path via dedicated pod and two new Kafka topics: `{env}-htg-docai-import` and `{env}-htg-docai-result`).

## Core Principles

- **Three execution types, one mapping pipeline.** Traditional (structured), AI/DocAI (LLM-based, async), and Unstructured (deferred to AiCOE) all flow through the same `IIngestionMapping` loop. Differences are isolated to the extraction layer and the deduction mapper keyed by `ExecutionType`.
- **Templates drive all field mapping.** No hardcoded column-to-field assignments. `ITemplateProvider` resolves rule table records at runtime; adding or changing a mapping requires no code change.
- **Slow extractions are async via Kafka.** Any extraction path that is slow or unreliable (DocAI, Unstructured) publishes to a dedicated topic and returns immediately. The kafka-processor consumer loop never blocks on a long I/O call.
- **DocAI is isolated in its own pod.** A hanging DocAI REST call cannot block Traditional or Unstructured processing. Kubernetes liveness probe restarts the pod if it becomes unresponsive.
- **SQS is a relay, not a processor.** `FileImportedSubscriber` only deserializes and re-publishes to Kafka, then deletes the SQS message. All processing logic is downstream.
- **Ordered mapper pipeline, not a monolith.** `ITimecardDistributionMapping` implementations run in `Order` sequence. Adding a new transformation = new class; no existing mappers change.
- **Deduction mappers are keyed, not switched.** `IDeductionDistributionMapping` implementations are registered with a key per `ExecutionType`; the mapping loop selects the right one without branching in shared code.
- **File tracking is log-based.** `IFileTrackerProvider` emits structured Serilog events with `ProcessName = DashboardFileTracking`. No separate tracking table.
- **Assembly scanning for mapper registration.** `AddDataIngestionMappings<Program>()` uses Scrutor to auto-discover all mapper implementations.

## Execution Types

| ExecutionType | Extraction path | Deduction mapper | Notes |
| --- | --- | --- | --- |
| Traditional | Inline in kafka-processor: `IDataIngestionService.ExtractDataAsync()` | `DeductionMappingTraditional` | Synchronous; no reimbursement generation |
| AI (DocAI) | Async via Kafka: kafka-processor publishes to `{env}-htg-docai-import`; docai-processor calls `IAIExtractionProvider.ExtractDataAsync()` and publishes result to `{env}-htg-docai-result`; kafka-processor maps | `DeductionMappingAI` | ~10 min call isolated in dedicated pod; unmatched deductions become `ReimbursementPaymentData` in baggage |
| Unstructured | Async via Kafka + external: kafka-processor publishes to `{env}-htg-aicoe-studio-import`; AiCOE service calls [Unstructured.io](http://Unstructured.io) and publishes to `{env}-aicoe-htg-unstructured-extract`; kafka-processor maps | `DeductionMappingUnstructured` | AI response stored in Redis; result published to `AicoFileImported` |

## Data Flow

### Traditional

```
StudioPlus (SNS → SQS)
  └─► FileImportedSubscriber [sqs-subscriber pod]
        └─► Kafka: {env}-htg-studio-import
              └─► FileImportedHandler [kafka-processor pod]
                    └─► ProcessFileProvider
                          ├─► IExtractionService → Traditional inline extraction
                          ├─► IIngestionMapping.MapAsync()
                          └─► IMessagePublishingService
                                ├─► IISSApiService.CreateBatchAsync()
                                └─► Kafka: HtgCapsTimecardExported
```

### AI / DocAI (async path — new)

The kafka-processor publishes to `{env}-htg-docai-import` and commits the offset immediately. The consumer loop is never blocked.

```
StudioPlus (SNS → SQS)
  └─► FileImportedSubscriber [sqs-subscriber pod]
        └─► Kafka: {env}-htg-studio-import
              └─► FileImportedHandler [kafka-processor pod]
                    └─► downloads file from StudioPlus → reads headers → determines path = DocAI   ← 1st download
                          └─► Kafka: {env}-htg-docai-import   ← offset committed here; consumer loop is free

{env}-htg-docai-import
  └─► DocAI consumer [docai-processor pod]
        └─► downloads file from StudioPlus again   ← 2nd download (needed to pass file content to DocAI API)
              └─► IAIExtractionProvider.ExtractDataAsync()   (~10 min · HTTP timeout · liveness probe)
                    └─► Kafka: {env}-htg-docai-result

{env}-htg-docai-result
  └─► DocAI result consumer [kafka-processor pod]
        └─► IIngestionMapping.MapAsync()
              └─► IMessagePublishingService
                    ├─► IISSApiService.CreateBatchAsync()
                    └─► Kafka: HtgCapsTimecardExported
```

> **Note for developer (H2G-6392):** The DocAI path downloads the file from StudioPlus **twice** — once in the kafka-processor to read the import template and determine the extraction path, and again in the docai-processor to pass the file content to the DocAI REST API. This means `HTGPlus.StudioPlusIntegration` must be registered in the docai-processor pod's DI container. The `FileImportedMessage` on `{env}-htg-docai-import` already carries the file key and file name needed for the second download. Passing raw file bytes through Kafka is not an alternative — payloads would be too large.

### Unstructured (AiCOE — unchanged)

```
StudioPlus (SNS → SQS)
  └─► FileImportedSubscriber [sqs-subscriber pod]
        └─► Kafka: {env}-htg-studio-import
              └─► FileImportedHandler [kafka-processor pod]
                    └─► Unstructured sheet detected
                          └─► Kafka: {env}-htg-aicoe-studio-import   ← offset committed; consumer loop free

AiCOE Service (external)
  └─► Kafka: {env}-aicoe-htg-unstructured-extract
        └─► FileExtractedHandler [kafka-processor pod, feature-flagged]
              ├─► IIngestionMapping.MapAsync()
              └─► Kafka: AicoCapsTimecardExported
```

## Shared Services Across Pod Modes

All three pods use the same Docker image. The following services are registered regardless of `RUN_MODE` because they are needed by more than one mode:

| Service | Required by |
| --- | --- |
| `IProcessFileRequestFactory` | sqs-subscriber: builds `FileImportedMessage` from SQS envelope body |
| `ICacheWraper` / `AddObjectCacheWrapper` | sqs-subscriber: via `IProcessFileRequestFactory` dependency |
| `IFileTrackerProvider` | sqs-subscriber: `TrackQueuedFile`; kafka-processor: `TrackInProgressFile`, `TrackErrorFile` |

All other services are mode-specific and only registered in their pod's `Configure*Services()` method in `Startup.cs`.

## Message Entry Points

### FileImportedSubscriber (SQS → Kafka relay) — sqs-subscriber pod

`BackgroundService`. Polls SQS on a configurable delay (default 10 s). Feature-flagged via `FeatureFlags:FileImportSubscriber`. For each message:

1. Deserializes SNS envelope (`MessageEnvelope<FileImportedEvent>`) from SQS body via `IProcessFileRequestFactory.GetMessage()`
2. Produces `FileImportedMessage` to Kafka `{env}-htg-studio-import` topic
3. Calls `IFileTrackerProvider.TrackQueuedFile()`
4. Deletes the SQS message

This service is a relay only — no file downloading or processing happens here.

### FileImportedHandler (Kafka consumer) — kafka-processor pod

`IMessageHandler<Message<string, FileImportedMessage>>`. Drives the main processing pipeline:

1. `IProcessFileRequestFactory.CreateRequestAsync()` — enriches message with ISS API data
2. `IFileTrackerProvider.TrackInProgressFile()`
3. `IProcessFileProvider.ProcessAsync()` — downloads the file from StudioPlus, reads headers/import template to determine extraction type, then routes: Traditional runs inline; AI/DocAI publishes to `{env}-htg-docai-import`; Unstructured publishes to `{env}-htg-aicoe-studio-import`
4. On error: `IFileTrackerProvider.TrackErrorFile()`

### DocAI Result Consumer (Kafka consumer) — kafka-processor pod

`IMessageHandler` on `{env}-htg-docai-result`. New consumer added by H2G-6392. Receives the extraction result from the docai-processor pod and runs the data mapping and publishing steps that previously followed the inline AI call.

### DocAI Consumer (Kafka consumer) — docai-processor pod

`IMessageHandler` on `{env}-htg-docai-import`. New consumer added by H2G-6391/6392. For each message:

1. Downloads the file from StudioPlus using the file key and file name carried in the `FileImportedMessage` — this is the **second download** of the file; the kafka-processor already downloaded it once to determine the extraction path
2. Calls `IAIExtractionProvider.ExtractDataAsync()` with configured HTTP timeout, passing the downloaded file content
3. Stores raw AI response in Redis (key: `ai_response_{worksheet}_{packageTrackingId}`, TTL 1 day)
4. Publishes extraction result to `{env}-htg-docai-result`

### FileExtractedHandler (Kafka consumer, feature-flagged) — kafka-processor pod

`IMessageHandler<Message<string, ExtractionResponse>>`. Registered only when `FeatureFlags:AicoeSheetExtractedSubscriber` is enabled. Handles the Unstructured flow after the external AiCOE service returns. Publishes to `AicoCapsTimecardExported`.

## Kafka Topic Reference

| Topic | Direction | Producer pod | Consumer pod | Notes |
| --- | --- | --- | --- | --- |
| `{env}-htg-studio-import` | Internal | sqs-subscriber | kafka-processor | `FileImported` alias |
| `{env}-htg-docai-import` | Internal | kafka-processor | docai-processor | **New** — DocAI async routing |
| `{env}-htg-docai-result` | Internal | docai-processor | kafka-processor | **New** — DocAI extraction result return |
| `{env}-htg-aicoe-studio-import` | To external | kafka-processor | AiCOE (external) | `AicoFileImported` alias |
| `{env}-aicoe-htg-unstructured-extract` | From external | AiCOE (external) | kafka-processor | `FileExtracted` alias; feature-flagged |
| `HtgCapsTimecardExported` | Outbound | kafka-processor | CAPS/DII | Traditional + AI/DocAI results |
| `AicoCapsTimecardExported` | Outbound | kafka-processor | CAPS/DII | Unstructured results |

## Extraction Layer (IExtractionService)

`GetWorksheetsAsync` always calls `IDataIngestionService.ExtractHeadersAsync()` regardless of execution type.

`ExtractDataAsync` branches on `ExecutionType`:

**Traditional** — inline in kafka-processor

- Calls `IDataIngestionService.ExtractDataAsync()` (structured Excel parser)
- Maps via `TraditionalDataIngestion.ExtractionResponseMapper`

**AI (DocAI)** — async; runs in docai-processor pod

- kafka-processor: downloads file from StudioPlus → reads headers → determines path is DocAI → publishes `FileImportedMessage` to `{env}-htg-docai-import` and commits offset
- docai-processor: **downloads file from StudioPlus a second time** using file key/name from the message; builds `ExtractionDataRequest` with `additionalColumns`, `ignoredColumns`, `customHeaderMappings`; calls `IAIExtractionProvider.ExtractDataAsync()`; stores raw response in Redis; maps via `AIDataIngestion.ExtractionResponseMapper`; publishes result to `{env}-htg-docai-result`
- kafka-processor: consumes result and runs mapping pipeline

**Unstructured** — async; runs in external AiCOE service

- `ExtractDataAsync` is never called for Unstructured sheets — `ProcessFileProvider` flags the sheet and publishes `AicoFileImported` instead
- When the result arrives on `FileExtracted`, `InternalExtractionResponseMapper` converts the unstructured response to the AI response shape so `AIDataIngestion.ExtractionResponseMapper` can be reused

## Core Mapping Pipeline (IIngestionMapping)

`MapAsync` runs for each worksheet result — identical across all execution types. Processing order:

1. Build `ImportFileResult` (metadata, batch fields)
2. Determine override destinations from template
3. For each `TimecardItem`:
    - Skip if no employee name and no full SSN
    - Run all `ITimecardExtractionMapping.ApplyMappings()` (pre-mapping)
    - For each `TimecardDistribution`: build `MapContext` → run all `ITimecardDistributionMapping` in `Order` → run `ITimecardPostMapping.MapAsync()` → call `IZipcodeProvider`
    - For each `DeductionDistribution`: select `IDeductionDistributionMapping` by `ExecutionType` key
    - For AI/Unstructured: generate reimbursement rows from baggage
4. Return `ImportFileResult` with full `List<Timecard>`

### Timecard Distribution Mappers (ITimecardDistributionMapping)

| Mapper | Order | Responsibility |
| --- | --- | --- |
| `BasePropertyMapping` | 0 | Scalar fields: occupation, cost code, pay type override, estimated gross, TK1–TK10, CPJN, notes, work start/end |
| `OccupationCodeMapping` | 0 | External occupation code → CAPS code via `DataMappingTableOccupation` rule table |
| `WorkLocationMapping` | 0 | Work location → CAPS location code + zip via `DataMappingTableWorkLocation` |
| `PayTypeMapping` | 99 | Pay types via `DataMappingTablePayType`; rate mapping; deduction override routing |

### Deduction Distribution Mappers (IDeductionDistributionMapping, keyed by ExecutionType)

| Key | Extra behavior |
| --- | --- |
| `Traditional` | Maps type/rate/units/calcType; looks up MappingDeductionId/Code |
| `AI` | Same lookup; if no match: sets `IgnoreDeduction=true`, adds `ReimbursementPaymentData` to baggage |
| `Unstructured` | Identical to AI |

## Publishing Layer (IMessagePublishingService)

`ProcessAndPublishBatchesAsync`:

1. Creates a DII batch record in ISS via `IISSApiService.CreateBatchAsync()`
2. If `ImportFileResult.IsError`: publishes a single error batch
3. Otherwise: splits timecards into batches of `BatchProcessingConfig.ItemsPerBatch` (default 100) and publishes each to Kafka
4. Returns `MessagePublishedResponse`

## File Tracking (IFileTrackerProvider)

Log-based — no separate database table. Emits structured Serilog events with a `ProcessName` discriminator.

| Method | Status | Pod |
| --- | --- | --- |
| `TrackQueuedFile()` | `Queued` | sqs-subscriber |
| `TrackInProgressFile()` | `InProgress` | kafka-processor |
| `TrackErrorFile()` | `Error` | kafka-processor |
| Sheet success | `SentToCaps` | kafka-processor |

## Infrastructure

### Redis

| Use | Key pattern | TTL | Pod |
| --- | --- | --- | --- |
| Arrival method list | (managed by `ICacheWraper`) | 24 h | kafka-processor |
| AI raw extraction response | `ai_response_{worksheet}_{packageTrackingId}` | 1 day | docai-processor |
| SignalR pub/sub notifications | — | — | kafka-processor |

### External Dependencies

| Dependency | Purpose | Pod |
| --- | --- | --- |
| `HTGPlus.StudioPlusIntegration` | File download, status update | kafka-processor, **docai-processor** (second download for DocAI path) |
| `HTGPlus.ISSIntegration` | EOR lookup, arrival methods, DII batch creation | kafka-processor, sqs-subscriber (via factory) |
| `HTGPlus.ExternalClientProviders` (IngestionService) | Traditional structured Excel extraction | kafka-processor |
| `HTGPlus.ExternalClientProviders` (ExtractionAI) | AI/LLM-based extraction | docai-processor |
| `HTGPlus.RuleCore` | Rule table matching engine | kafka-processor |
| `HTGPlus.KafkaCore` / `KafkaCore.MultiConsumer` | Kafka producer/consumer abstractions | all pods |
| AWS SQS | Inbound file import event queue from StudioPlus | sqs-subscriber |
| Redis | Arrival method cache, AI response store, SignalR | kafka-processor, docai-processor |
| SQL Server | Zipcode/state lookup, rule table data | kafka-processor |

## What Is Not Yet Built

- ☐ `HtgFileImportedSubscriber` — legacy HTG-internal Kafka subscriber is commented out; not yet migrated to the consolidated handler pattern
- ☐ Rate expression coverage — `IRateExpressionMapper` supports `fixRate(n)` and `rateDividedBy(n)`; unmapped expressions fall through silently
- ☐ Unstructured deduction-to-reimbursement end-to-end — `DeductionMappingUnstructured` mirrors AI logic but the `FileExtractedHandler` path has limited end-to-end test coverage
- ☐ Batch date validation — no `ResolveBatchWeekEndingDate` fallback guard
- ☐ Liveness probe on docai-processor — Kubernetes health probe to trigger pod restart when the DocAI call hangs past its timeout. Tracked in [H2G-6393](https://castandcrew.atlassian.net/browse/H2G-6393)
- ☐ Circuit breaker on DocAI HTTP client — if DocAI API is consistently unavailable, successive timeouts will fill `{env}-htg-docai-import` with unprocessable messages. A Polly circuit breaker would back off after N consecutive failures. Tracked in [H2G-6393](https://castandcrew.atlassian.net/browse/H2G-6393)

## Key Design Decisions

| Decision | Rationale |
| --- | --- |
| DocAI in its own pod | The DocAI REST call takes ~10 min and hangs intermittently. Isolating it means a hung call never blocks Traditional or Unstructured processing. Kubernetes can restart the DocAI pod independently. |
| DocAI async via Kafka (mirrors Unstructured pattern) | Keeps the kafka-processor consumer loop always fast. The kafka-processor commits the offset as soon as it publishes to `htg-docai-import`. Consistent with how Unstructured is handled via AiCOE. |
| DocAI path downloads file twice | The kafka-processor downloads the file to read headers and determine the extraction path. The docai-processor then downloads it again to pass to the DocAI API. Passing raw file bytes through Kafka is not an option — payloads would be too large. Both pods need `HTGPlus.StudioPlusIntegration` registered. |
| sqs-subscriber fixed at 1 replica | Multiple replicas would race to consume the same SQS messages, producing duplicate Kafka events. The relay is inherently single-writer. |
| kafka-processor is scalable | Kafka consumer group semantics handle multiple instances cleanly. Scale based on consumer lag on `{env}-htg-studio-import`. |
| docai-processor starts at 1 replica | DocAI file volume is expected to be low. If volume grows, the pod can scale — Kafka consumer group semantics apply. |
| SQS is a relay to Kafka, not a processor | Decouples StudioPlus event delivery from processing. Kafka gives replay, backpressure, and multi-consumer routing that SQS alone does not. |
| Template-driven mapping via rule tables | No code changes needed to add or modify a field mapping — templates are configured in the rule table UI. |
| Ordered `ITimecardDistributionMapping` pipeline | Mappers are independent and composable. Adding a new transformation = new class; no existing mappers change. |
| `IDeductionDistributionMapping` keyed by `ExecutionType` | Avoids branching in shared mapping code. Each execution type owns its full deduction behavior. |
| File tracking is log-based | Avoids an extra database dependency. Dashboard queries use structured log aggregations (Elasticsearch). |
| AI raw response stored in Redis | AI payloads can be large; storing out-of-band keeps Kafka message payload small and allows re-processing without re-calling the AI service. |
| `FileExtractedHandler` feature-flagged | Unstructured/AiCOE flow is independently deployable without a redeploy. |
| Assembly scanning via Scrutor | Adding a mapper class is sufficient — no manual `services.AddScoped<>` needed. |

## Related Jira Tickets

| Ticket | Summary | Status |
| --- | --- | --- |
| [H2G-6390](https://castandcrew.atlassian.net/browse/H2G-6390) | Add HTTP timeout to DocAI REST client | Open |
| [H2G-6391](https://castandcrew.atlassian.net/browse/H2G-6391) | Introduce `RUN_MODE=docai-processor` pod — infrastructure only | Open |
| [H2G-6392](https://castandcrew.atlassian.net/browse/H2G-6392) | Route DocAI messages through dedicated pod (activate async path) | Open |
| [H2G-6393](https://castandcrew.atlassian.net/browse/H2G-6393) | Harden docai-processor pod (liveness probe + circuit breaker) | Open |
