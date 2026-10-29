# ExcelMapping Redesign — Architecture Overview

Source: Confluence page 5412192268, space HPS, last modified sept 01, 2026.

This document is intended for readers who are new to the ExcelMapping system. It explains the current design, why it needs to change, and how the new architecture works — including how we migrate safely without disrupting production.

## Background: What is ExcelMapping?

When a production company submits a timecard spreadsheet (Excel file), our system processes it end-to-end and produces a payroll batch in CAPS (Cast & Crew's CAPS payroll platform). This pipeline is called **ExcelMapping**.

Today, this entire pipeline runs inside a single service called **kafka-processor**. It handles every step in sequence:

1. Receives a notification that a file has been uploaded
2. Determines what type of file it is (Traditional, DocAI, or Unstructured)
3. Extracts the data from the file using an external API
4. Maps the extracted data to a payroll batch using business rules
5. Submits the batch to CAPS

*Note on Unstructured files (current design): When kafka-processor identifies a sheet as Unstructured, it does not extract the data itself. Instead it publishes to {env}-htg-aicoe-studio-import, AiCOE processes the file externally, and returns the result on aicoe-htg-unstructured-extract. A second handler inside kafka-processor (FileExtractedHandler) then picks up that result and submits the payroll batch. The new design moves this same AiCOE round-trip into TemplateDecider (outbound) and MappingProcessor's UnstructuredMappingHandler (inbound).*

## The Problem with the Current Design

Bundling all five steps into one service creates several problems:

- **No independent scaling.** If DocAI extraction is slow (it calls an external AI API), it holds up Traditional extraction too — they share the same processing thread pool. We cannot scale one without scaling the other.
- **A bug in one step can take down the whole pipeline.** An error in the mapping logic can cause file extraction to stop as well.
- **Hard to test and maintain.** Each step has different dependencies, different failure modes, and different performance characteristics. Mixing them together makes the code harder to reason about and harder to change safely.
- **Difficult to onboard new file types.** Adding support for Unstructured files (handled by an external team, AiCOE) requires changing the same monolithic service that handles everything else.

## The New Architecture

We are splitting kafka-processor into three focused services, each responsible for exactly one concern. All three run as separate Kubernetes pods from the same codebase, controlled by a `RUN_MODE` environment variable — no new codebases to maintain.

| Service | Responsibility | Input | Output |
| --- | --- | --- | --- |
| **TemplateDecider** | Looks at the incoming file and decides which processing path it belongs to (Traditional, DocAI, or Unstructured) | File upload notification (Kafka) | Routes the file to the correct downstream topic |
| **ExtractProcessor** | Downloads the file and calls the appropriate extraction API to pull structured data out of it | Routed file message (Kafka) | Extracted data (Kafka) |
| **MappingProcessor** | Takes extracted data and applies business rules to produce a payroll batch, then submits it to CAPS | Extracted data (Kafka) | Payroll batch submitted to CAPS |

*Note: A fourth pod — RUN\_MODE=sqs-subscriber — sits at the entry point of the pipeline and is shared by both the old and new designs. It reads file-upload events from AWS SQS and publishes them to the FileImported Kafka topic. All other pods (old kafka-processor and new TemplateDecider) read from that same topic via separate consumer groups. The SQS Subscriber requires no changes during this migration.*

### Message Flow (New Design)

**Before (today):** File upload → kafka-processor (all steps) → CAPS

**After (new design):**

1. File upload → **TemplateDecider** → routes to correct topic
2. Correct topic → **ExtractProcessor** → extracted data topic
3. Extracted data topic → **MappingProcessor** → CAPS

Each step communicates via Kafka topics. If one service is slow or restarted, the others keep running and messages wait safely in the queue.

## Why This Approach?

### Why Kafka between each step?

Kafka acts as a durable buffer between services. If ExtractProcessor goes down for a few minutes, no files are lost — they wait in the queue and are processed when it comes back up. This is the same pattern already used throughout the HTG platform.

### Why three separate pods instead of one?

Each concern has a different resource profile. TemplateDecider is lightweight (just a routing decision). ExtractProcessor is I/O-bound (calls external APIs). MappingProcessor is CPU-bound (rule evaluation). Separating them lets us scale each independently based on load, and a crash in one does not affect the others.

### Why not a big-bang rewrite?

We run the old and new services in parallel during validation. The old kafka-processor keeps processing files normally while the new services are tested alongside it. This means zero downtime and a simple rollback if anything goes wrong.

## Migration Strategy

The migration happens in five tickets, in sequence:

| Ticket | What happens | Production impact |
| --- | --- | --- |
| [H2G-6391](https://castandcrew.atlassian.net/browse/H2G-6391) | Deploy TemplateDecider. It runs permanently alongside the old service — both receive every file independently via separate Kafka consumer groups. | None. Old service unchanged. |
| [H2G-6453](https://castandcrew.atlassian.net/browse/H2G-6453) | Deploy ExtractProcessor. Smoke test (one Traditional file, one DocAI file), then scale back to 0. | External extraction APIs called twice per file during smoke test window only. |
| [H2G-6454](https://castandcrew.atlassian.net/browse/H2G-6454) | Deploy MappingProcessor. Before deploying, reduce Kafka retention on {env}-htg-traditional-extract and {env}-htg-docai-extract to 1 hour to prevent MappingProcessor from replaying all messages accumulated since H2G-6453 deployed. Smoke test (coordinate with AiCOE for Unstructured), then scale back to 0. Ask CAPS to delete the duplicate batch from smoke test. | One duplicate CAPS batch during smoke test window only. |
| [H2G-6392](https://castandcrew.atlassian.net/browse/H2G-6392) | Cutover: scale MappingProcessor to 1 and old kafka-processor to 0 at the same time. Validate end-to-end. | Two-command atomic swap. Rollback = swap back. |
| [H2G-6455](https://castandcrew.atlassian.net/browse/H2G-6455) | Remove old kafka-processor code after 2+ weeks of stable production. Scheduled for next regular release. | None. Code-only cleanup. |

## Feature Flag: useNewExcelImportNewPipeline

TemplateDecider's behavior is partially controlled by the Unleash feature flag *useNewExcelImportNewPipeline*. When this flag is ON, the decider:

- Updates the file status to Processing when it begins routing a file
- Routes Unstructured sheets to the AicoFileImported topic (triggering the AiCOE round-trip)
- Updates the file status to Submitted when no matching template is found  

When the flag is OFF, those three side effects are skipped and Unstructured sheets are routed to the UnstructuredImport topic instead.

During smoke testing and cutover (H2G-6453, H2G-6454, H2G-6392), confirm this flag is in the expected state before validating results.

**Feature flag flow reference**

`useNewExcelImportNewPipeline` controls three behaviors inside `TemplateDeciderRouteProvider`; all other routing behavior remains unchanged.

**Flag ON**

- Update file status to **Processing**
- Route Unstructured sheets to `{env}-htg-aicoe-studio-import` (`AicoFileImported`)
- On no template match, update file status to **Submitted**

Triggers the AiCOE external extraction round-trip.

**Flag OFF**

- Skip the **Processing** status update
- Route Unstructured sheets to `{env}-htg-aicoe-unstructured-import` (`UnstructuredImport`)
- Skip the no-match **Submitted** status update

Safe shadow-run path; the Decider does not update file status.

### Behavior comparison

| Behavior | Flag ON | Flag OFF |
| --- | --- | --- |
| UpdateFileStatus → Processing | Called | Skipped |
| Unstructured route | `AicoFileImported` `{env}-htg-aicoe-studio-import` | `UnstructuredImport` `{env}-htg-aicoe-unstructured-import` |
| UpdateFileStatus → Submitted (no match) | Called | Skipped |

**Shared flow:** `FileImported` received → match template per sheet → Traditional and DocAI routes remain unchanged.

**Code reference:** `HTGPlus.ExcelMapping/TemplateDecider/TemplateDeciderRouteProvider.cs`

## Rollback Plan

At any point before the code removal ticket (H2G-6455), rollback is two commands:

1. Scale old kafka-processor back to 1 replica
2. Scale MappingProcessor back to 0 replicas

kafka-processor resumes from its last committed Kafka offset immediately. No data is lost.

## What We Gain

- **Independent scaling per concern** — scale ExtractProcessor when DocAI volume spikes without touching the rest of the pipeline
- **Isolated failures** — a bug in mapping logic does not interrupt file extraction
- **Cleaner onboarding for new file types** — adding a new type means adding a consumer to ExtractProcessor and MappingProcessor; TemplateDecider gets a new routing rule
- **Zero-downtime migration** — parallel running and atomic cutover mean no service window required

## Key Dependencies

- **AiCOE team** — The flow is bidirectional. TemplateDecider triggers AiCOE by publishing to the {env}-htg-aicoe-studio-import topic. AiCOE processes the file and returns the extracted result on the aicoe-htg-unstructured-extract topic. MappingProcessor's UnstructuredMappingHandler consumes from that return topic.
- **CAPS team** — asked to delete one duplicate payroll batch after the MappingProcessor smoke test.

## Related Documents

- [ExcelMapping Target Redesign](https://castandcrew.atlassian.net/wiki/spaces/HPS/pages/5412749317/HTGPlus.ExcelMapping+Target+Redesign+Concern-Separated+Pods) — detailed technical specification (developer audience)
- [H2G-2052](https://castandcrew.atlassian.net/browse/H2G-2052) — parent epic: Excel Import Mapping Phase 2
