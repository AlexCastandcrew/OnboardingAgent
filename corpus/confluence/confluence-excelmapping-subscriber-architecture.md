# HTGPlus.ExcelMapping.Subscriber — Architecture Design

Source: Confluence page 5385224223, space HPS, last modified jul 22, 2026.

**Status:** In Progress   **Last Updated:** 2026-07-18

## Related Documents

- [ALES Timecard Ingestion — Excel File Flow](https://castandcrew.atlassian.net/wiki/spaces/HPS/pages/5199953928/ALES+Timecard+Ingestion+Excel+File+Flow) — end-to-end business flow: service responsibilities, Kafka topics, extraction type overview

## Purpose

HTGPlus.ExcelMapping.Subscriber bridges StudioPlus (an external payroll platform) and HTG's internal payroll system (CAPS/DII). It consumes file-import events from AWS SQS and Apache Kafka, downloads uploaded Excel/CSV files, maps their contents to a canonical payroll format using a rule-table-driven template engine, and publishes the mapped timecards back to Kafka for downstream consumers.

[diagram omitted]

*Figure 1 — End-to-end ALES Excel file ingestion flow.*

[diagram omitted]

Figure 2 V2- NEW RUN\_MODE

## Core Principles

- **Three execution types, one mapping pipeline.** Traditional (structured extraction), AI (LLM-based extraction), and Unstructured (deferred to external AICOE service) all flow through the same `IIngestionMapping` loop. Differences are isolated to the extraction layer and the deduction mapper keyed by `ExecutionType`.
- **Templates drive all field mapping.** There are no hardcoded column-to-field assignments. `ITemplateProvider` resolves an `AssignImportTemplate` rule table record → `ImportTemplate` rule table record → `TemplateCellInfo` rows. All mapping items, column headers, and execution type are read from the template at runtime.
- **Ordered mapper pipeline, not a monolith.** `ITimecardDistributionMapping` implementations run in `Order` sequence (0 = base/occupation/work-location, 99 = pay types last). Each mapper reads from `MapContext` and writes back — no mapper knows about its neighbors.
- **Deduction mappers are keyed, not switched.** `IDeductionDistributionMapping` implementations are registered with a key per `ExecutionType`; the mapping loop selects the right one without branching on execution type in shared code.
- **SQS is a relay, not a processor.** `FileImportedSubscriber` (SQS) only deserializes and re-publishes to Kafka, then deletes the SQS message. All processing logic lives downstream in `FileImportedHandler` (Kafka consumer).
- **File tracking is log-based, not database-based.** `IFileTrackerProvider` emits structured Serilog events with `ProcessName = DashboardFileTracking`. No separate tracking table.
- **Rule tables drive all code mapping.** Occupation, work location, pay type, deduction, and rate mappings all use `IRuleTableMatchProvider` → `DataMappingTable*` rule table records. No switch statements on external codes.
- **Assembly scanning for mapper registration.** `AddDataIngestionMappings<Program>()` uses Scrutor to auto-discover all `ITimecardDistributionMapping`, `IDeductionDistributionMapping`, `IMappingItemParser`, etc. Adding a new mapper class is sufficient — no manual service registration needed.

## Execution Types

| ExecutionType | Extraction service | Deduction mapper | Notes |
| --- | --- | --- | --- |
| Traditional | `IDataIngestionService.ExtractDataAsync()` (structured) | `DeductionMappingTraditional` | No reimbursement generation |
| AI | `IAIExtractionProvider.ExtractDataAsync()` (LLM) | `DeductionMappingAI` | Unmatched deductions become `ReimbursementPaymentData` in baggage |
| Unstructured | Deferred — sheet skipped in `ProcessFileProvider`; `FileExtractedHandler` handles result on `FileExtracted` topic | `DeductionMappingUnstructured` | AI response stored in Redis; result published to `AicoFileImported` |

## Folder Structure

```
HTGPlus.ExcelMapping/
├── Program.cs
├── Startup.cs
├── Common/
│   ├── Constants.cs
│   ├── ExcelMappingTopics.cs
│   ├── ImportFileProcessHelper.cs
│   ├── RegexHelper.cs                    (source-generated regexes)
│   └── Extensions/
│       ├── BooleanExtensions.cs
│       ├── DateTimeExtensions.cs
│       ├── DictionaryExtensions.cs
│       └── MapperExtensions.cs
├── Services/
│   ├── FileImportedSubscriber.cs         (SQS BackgroundService — relay only)
│   ├── FileImportedHandler.cs            (Kafka consumer — FileImported)
│   ├── HtgFileImportedSubscriber.cs      (legacy HTG subscriber, commented out)
│   ├── ProcessFileRequestFactory.cs
│   ├── ProcessFileProvider.cs            (orchestrator)
│   ├── BaseProcessProvider.cs            (shared sheet processing)
│   ├── MessagePublishingService.cs
│   ├── TemplateProvider.cs
│   ├── HeadersProvider.cs
│   ├── RuleTableMatchProvider.cs
│   ├── FileTrackerProvider.cs
│   ├── AIDataProvider.cs
│   ├── ZipcodeProvider.cs
│   └── Sqs/
│       ├── SQSProvider.cs
│       ├── MessageEnvelope.cs
│       └── ServiceCollection.cs
├── DataIngestionMapping/
│   ├── IngestionMapping.cs               (IIngestionMapping — core mapping loop)
│   ├── Mappers/
│   │   ├── BasePropertyMapping.cs        (Order=0)
│   │   ├── OccupationCodeMapping.cs      (Order=0)
│   │   ├── WorkLocationMapping.cs        (Order=0)
│   │   ├── PayTypeMapping.cs             (Order=99)
│   │   ├── CustomExpressionMapper.cs     (ITimecardExtractionMapping)
│   │   ├── PayTypeOverrideExtractionMapping.cs
│   │   ├── WorkLocationOverrideExtractionMapping.cs
│   │   └── TimecardPostMapping.cs        (ITimecardPostMapping)
│   ├── Parsers/
│   │   └── TemplateParsers/
│   │       ├── MappingItemFactory.cs
│   │       ├── ConcatMappingItemParser.cs
│   │       ├── OrMappingItemParser.cs
│   │       └── SetFieldMappingItemParser.cs
│   ├── Services/
│   │   └── ExtractionService.cs
│   ├── TraditionalDataIngestion/
│   │   └── ExtractionResponseMapper.cs
│   └── AIDataIngestion/
│       ├── ExtractionResponseMapper.cs
│       └── DeductionMappers/
│           ├── BaseDeductionMapping.cs
│           ├── DeductionMappingTraditional.cs
│           ├── DeductionMappingAI.cs
│           └── DeductionMappingUnstructured.cs
├── UnestructuredExtraction/
│   ├── FileExtractedHandler.cs           (Kafka consumer — FileExtracted, feature-flagged)
│   ├── InternalExtractionResponseMapper.cs
│   └── ResultExtractionResponseMapper.cs
└── Models/
    ├── ProcessFileRequest.cs
    ├── ImportFileResult.cs
    ├── Metadata.cs
    ├── Timecard.cs
    ├── TimecardDistribution.cs
    ├── DeductionDistribution.cs
    ├── MatchingAssignedTemplate.cs
    ├── TemplateInfo.cs
    ├── MappingItem.cs
    └── FileImportedMessage.cs
```

## Data Flow

### Traditional / AI

SQS receives the event from StudioPlus, forwards it to Kafka, and the Kafka consumer drives all processing.

```
StudioPlus (SNS/SQS)
  └─► FileImportedSubscriber (SQS BackgroundService — relay only)
        └─► Kafka: FileImported topic
              └─► FileImportedHandler (Kafka consumer)
                    └─► ProcessFileRequestFactory.CreateRequestAsync()
                          └─► ProcessFileProvider.ProcessAsync()
                                ├─► IStudioPlusFileService.DownloadFileAsync()
                                ├─► IExtractionService.GetWorksheetsAsync()   (if no sheets in request)
                                └─► per worksheet:
                                      ├─► ITemplateProvider.GetMatchingAssignedTemplateAsync()
                                      ├─► IBaseProcessProvider.ProcessSheetAsync()
                                      │     ├─► IExtractionService.ExtractDataAsync()
                                      │     └─► IIngestionMapping.MapAsync()
                                      └─► IBaseProcessProvider.ProcessImportFileSheetResultAsync()
                                            └─► IMessagePublishingService.ProcessAndPublishBatchesAsync()
                                                  ├─► IISSApiService.CreateBatchAsync()
                                                  └─► Kafka: HtgCapsTimecardExported
```

### Unstructured (AICOE)

The sheet is detected as Unstructured during template resolution and handed off to an external AI service. The result returns on a separate Kafka topic.

```
StudioPlus (SNS/SQS)
  └─► FileImportedSubscriber → Kafka: FileImported
        └─► FileImportedHandler → ProcessFileProvider
              └─► Unstructured sheet detected
                    └─► Kafka: AicoFileImported   (triggers external AICOE service)

AICOE service (external)
  └─► Kafka: FileExtracted topic
        └─► FileExtractedHandler (Kafka consumer, feature-flagged)
              ├─► IBaseProcessProvider.ProcessSheetAsync()
              └─► IBaseProcessProvider.ProcessImportFileSheetResultAsync()
                    └─► Kafka: AicoCapsTimecardExported
```

## Message Entry Points

### FileImportedSubscriber (SQS → Kafka relay)

`BackgroundService`. Polls SQS on a configurable delay (default 10 s). Feature-flagged via `FeatureFlags:FileImportSubscriber`. For each message:

1. Deserializes SNS envelope (`MessageEnvelope<FileImportedEvent>`) from SQS body
2. Produces to Kafka `FileImported` topic via `IKafkaProducer<string, FileImportedMessage>`
3. Calls `IFileTrackerProvider.TrackQueuedFile()`
4. Deletes the SQS message

This service is a relay only — no file downloading or processing happens here.

### FileImportedHandler (Kafka consumer)

`IMessageHandler<Message<string, FileImportedMessage>>`. Registered unconditionally. Drives the main processing pipeline:

1. `IProcessFileRequestFactory.CreateRequestAsync()` — enriches message with ISS API data
2. `IFileTrackerProvider.TrackInProgressFile()`
3. `IProcessFileProvider.ProcessAsync()`
4. On error: `IFileTrackerProvider.TrackErrorFile()`

### FileExtractedHandler (Kafka consumer, feature-flagged)

`IMessageHandler<Message<string, ExtractionResponse>>`. Registered only when `FeatureFlags:AicoeSheetExtractedSubscriber` is enabled. Handles the Unstructured flow after the external AICOE service returns:

1. `IProcessFileRequestFactory.CreateProcessFileRequestAsync()` — builds request from extraction metadata
2. For each worksheet: `IBaseProcessProvider.ProcessSheetAsync()` + `ProcessImportFileSheetResultAsync()`
3. Publishes to `AicoCapsTimecardExported` topic

## Request Factory (IProcessFileRequestFactory)

Builds `ProcessFileRequest` with all metadata needed by `ProcessFileProvider`. Three factory methods:

| Method | Used by | Source detection |
| --- | --- | --- |
| `CreateRequestAsync(body)` | `FileImportedHandler` | Checks for `MetaAttributes` (HTG) vs. `ClientId` pipe-split (StudioPlus) |
| `CreateHtgRequest(body)` | Legacy HTG subscriber (commented out) | Always HTG |
| `CreateProcessFileRequestAsync(metadata)` | `FileExtractedHandler` | Uses metadata keys if already present; otherwise builds from `clientId`/`projectId` |

ISS API calls during `CreateRequestAsync`:

- `GetEorByClientIdAsync()` — resolves EOR / database code for the client
- `GetClientsAsync()` — fetches parent client info
- `GetArrivalMethodsAsync()` — **cached 24 hours in Redis**
- `SetClientTypeInMetadataAsync()` — fetches `clientType` from ISS and writes into metadata dictionary

## Template Resolution (ITemplateProvider)

Two-step rule table lookup followed by reflection-based mapping item parsing:

1. `AssignImportTemplate` rule table matched on database, client type, parent client, client, project, worksheet name → returns template name
2. `ImportTemplate` rule table matched on template name → returns column/field mappings and `ExecutionType`
3. `RuleTableCells` SQL query for `TemplateCellInfo` (pay-type-specific column names)
4. Reflects over `ImportTemplate` result properties; each property with a value becomes a `MappingItem` via `IMappingItemFactory`
5. `IHeadersProvider` parses `workbook.worksheets[n].headers` from the template's Action JSON blob

Returns `MatchingAssignedTemplate { TemplateInfo, ImportTemplate }`.

### Mapping Item Expressions

`IMappingItemFactory` tries each registered `IMappingItemParser` before falling back to a plain header list:

| Parser | Pattern | Result |
| --- | --- | --- |
| `ConcatMappingItemParser` | `concat('sep','f1','f2',...)` | `FieldExpressionType.Concat` |
| `OrMappingItemParser` | `OR('f1','f2',...)` | `FieldExpressionType.Or` |
| `SetFieldMappingItemParser` | `setField('value')` | `FieldExpressionType.SetField` |

All regex patterns are source-generated in `RegexHelper.cs`.

## Extraction Layer (IExtractionService)

`GetWorksheetsAsync` always calls `IDataIngestionService.ExtractHeadersAsync()` regardless of execution type.

`ExtractDataAsync` branches on `ExecutionType`:

**Traditional**

- Calls `IDataIngestionService.ExtractDataAsync()` (structured Excel parser)
- Maps via `TraditionalDataIngestion.ExtractionResponseMapper`

**AI**

- Builds `ExtractionDataRequest` with `additionalColumns` (from template mappings), `ignoredColumns` (from metadata), `customHeaderMappings` (for OR expressions)
- Calls `IAIExtractionProvider.ExtractDataAsync()`
- Stores raw AI response in Redis via `IAIDataProvider` (key: `ai_response_{worksheet}_{packageTrackingId}`, TTL 1 day)
- Maps via `AIDataIngestion.ExtractionResponseMapper`

**Unstructured**

- `ExtractDataAsync` is never called for Unstructured sheets — `ProcessFileProvider` flags the sheet and publishes `AicoFileImported` instead
- When the result arrives on `FileExtracted`, `InternalExtractionResponseMapper` converts the unstructured response to the AI response shape so `AIDataIngestion.ExtractionResponseMapper` can be reused

## Core Mapping Pipeline (IIngestionMapping)

`MapAsync` runs for each worksheet result. Processing order:

1. Build `ImportFileResult` (metadata, batch fields)
2. Determine override destinations from template (`payTypeOverrideDestination`, `deductionOverrideDestination`)
3. For each `TimecardItem` from extraction:
    - Skip if no employee name and no full SSN
    - Run all `ITimecardExtractionMapping.ApplyMappings()` (pre-mapping)
    - For each `TimecardDistribution`:
        - Build `MapContext` (flattened row dictionary + template + metadata)
        - Run all `ITimecardDistributionMapping` in `Order` sequence
        - Run `ITimecardPostMapping.MapAsync()` (override post-processing)
        - Call `IZipcodeProvider.GetZipcodeAndForeignFlagAsync()`
    - For each `DeductionDistribution`: select `IDeductionDistributionMapping` by `ExecutionType` key
    - For AI/Unstructured: generate reimbursement `TimecardDistribution` rows from `DeductionDistributionBaggage.ReimbursementPayments`
4. Return `ImportFileResult` with full `List<Timecard>`

### Timecard Distribution Mappers (ITimecardDistributionMapping)

Run in `Order` sequence on every `TimecardDistribution`.

| Mapper | Order | Responsibility |
| --- | --- | --- |
| `BasePropertyMapping` | 0 | Scalar fields: occupation, cost code, pay type override, estimated gross, deduction override, TK1–TK10, CPJN, notes, work start/end |
| `OccupationCodeMapping` | 0 | External occupation code → CAPS code via `DataMappingTableOccupation` rule table |
| `WorkLocationMapping` | 0 | Work location → CAPS location code + zip via `DataMappingTableWorkLocation`; handles static override, multi-column override list, per-execution-type field priority |
| `PayTypeMapping` | 99 | Pay types via `DataMappingTablePayType`; pay type override; rate mapping (`DataMappingTableRate`, `IRateExpressionMapper`); deduction override routing to `DeductionDistributions` |

### Extraction Pre-Mappers (ITimecardExtractionMapping)

Run once per `TimecardItem` before the distribution loop.

| Mapper | Responsibility |
| --- | --- |
| `CustomExpressionMapper` | Evaluates `concat(...)` expressions across `TimecardItem` and `TimecardDistribution` field dictionaries |
| `PayTypeOverrideExtractionMapping` | Reads configured PayTypeOverride source column → writes to `TimecardDistribution.PayTypeOverride` |
| `WorkLocationOverrideExtractionMapping` | Reads configured WorkLocationOverride source columns → builds `WorkLocationOverride` list |

### Post-Mapper (ITimecardPostMapping)

Runs after all distribution mappers on each `TimecardDistribution`:

- **Pay type override**: if override was matched, propagates Occupation / WorkLocation / TK1 from previous distribution per `payTypeOverrideDestination`; otherwise removes dangling unmatched pay type entries
- **Deduction override**: same logic for `deductionOverrideDestination`

### Deduction Distribution Mappers (IDeductionDistributionMapping, keyed by ExecutionType)

All extend `BaseDeductionMapping` which wraps `DataMappingTableDeduction` rule table matching.

| Key | Extra behavior |
| --- | --- |
| `Traditional` | Maps type/rate/units/calcType; looks up MappingDeductionId/Code |
| `AI` | Same lookup; if no match: sets `IgnoreDeduction=true`, runs pay type rule table, adds `ReimbursementPaymentData` to baggage |
| `Unstructured` | Identical to AI |

## Publishing Layer (IMessagePublishingService)

`ProcessAndPublishBatchesAsync`:

1. Creates a DII batch record in ISS via `IISSApiService.CreateBatchAsync()` (FirstEarnedDate, LastEarnedDate, EstimatedGross, ExpectedTimecardCount, etc.)
2. If `ImportFileResult.IsError`: publishes a single error batch
3. Otherwise: splits timecards into batches of `BatchProcessingConfig.ItemsPerBatch` (default 100) and publishes each to Kafka `HtgCapsTimecardExported`
4. Returns `MessagePublishedResponse` (success count, failed count, BatchId)

### Kafka Topic Aliases

| Alias | Direction | Consumer |
| --- | --- | --- |
| `FileImported` | Inbound | `FileImportedHandler` |
| `FileExtracted` | Inbound | `FileExtractedHandler` (feature-flagged) |
| `HtgCapsTimecardExported` | Outbound | CAPS/DII — Traditional + AI |
| `AicoCapsTimecardExported` | Outbound | CAPS/DII — Unstructured |
| `AicoFileImported` | Outbound | External AICOE service |

## File Tracking (IFileTrackerProvider)

Log-based — no separate database table. Emits structured Serilog events with a `ProcessName` discriminator.

| Method | Status value | ProcessName |
| --- | --- | --- |
| `TrackQueuedFile()` | `Queued` | `DashboardFileTracking` |
| `TrackInProgressFile()` | `InProgress` | `DashboardFileTracking` |
| `TrackErrorFile()` | `Error` | `DashboardFileTracking` / `DashboardSheetTracking` |
| Sheet success | `SentToCaps` | `DashboardSheetTracking` |

Dashboard observability queries filter on `ProcessName` and `FileTrackingStatus` in the Elasticsearch log index.

## Infrastructure

### SQS (ISQSProvider)

Singleton. `ReceiveMessagesAsync`, `DeleteMessageAsync`, `DelayMessageAsync` (visibility timeout extension), `GetQueueUrlAsync` (cached in `ConcurrentDictionary`). Optional custom `ServiceURL` for local dev.

### Zipcode (IZipcodeProvider)

Singleton. Queries SQL Server (`dbo.US_Zipcodes`, `dbo.US_Cities`, `dbo.US_States`) via `ObjectCache.ExecuteAsync`. Returns `USZipcodeResult` (zipcode, country, `isForeign` flag).

### Redis

Three uses:

- Arrival method list — 24-hour TTL via `ICacheWraper`
- AI raw extraction response — 1-day TTL via `IAIDataProvider` (key: `ai_response_{worksheet}_{packageTrackingId}`)
- SignalR pub/sub notifications to the uploading user via `BaseProcessProvider.SendNotificationAsync`

### External Dependencies

| Dependency | Purpose |
| --- | --- |
| `HTGPlus.StudioPlusIntegration` | File download (`DownloadFileAsync`), status update (`UpdateFileStatusAsync`) |
| `HTGPlus.ISSIntegration` | EOR lookup, parent client, arrival methods, DII batch creation (`CreateBatchAsync`) |
| `HTGPlus.ExternalClientProviders` (IngestionService) | Traditional structured Excel extraction |
| `HTGPlus.ExternalClientProviders` (ExtractionAI) | AI/LLM-based extraction |
| `HTGPlus.RuleCore` | Rule table matching engine for template assignment, occupation, work location, pay type, deduction, rate |
| `HTGPlus.KafkaCore` / `KafkaCore.MultiConsumer` | Kafka producer/consumer abstractions |
| AWS SQS | Inbound file import event queue from StudioPlus |
| Redis | Arrival method cache, AI response store, SignalR pub/sub |
| SQL Server | Zipcode/state lookup, rule table data |

## Common Model: ProcessFileRequest

```c#
public record ProcessFileRequest
{
    public string FileKey { get; init; }
    public string FileName { get; init; }
    public Dictionary<string, object> MetaDataDictionary { get; init; }

    // Resolved from metadata dictionary
    public string PackageTrackingId { get; }
    public Starrable<BaseModel?> Eor { get; }
    public Starrable<BaseModel?> ParentClient { get; }
    public Starrable<BaseModel?> Client { get; }
    public Starrable<BaseModel?> Project { get; }
    public IEnumerable<string> WorkSheets { get; }
    public string Username { get; }
    public string UserEmail { get; }
    public string UploadedBy { get; }

    public bool IsValidPayload();
    public bool IsFromOrigin(string origin);
    public bool TryGetFromMetadata<T>(string key, out T value);
}
```

`TryGetFromMetadata<T>` handles `JsonElement`, Newtonsoft tokens, direct casts, and StudioPlus → RuleCore `BaseModel` conversion — all callers use the same dictionary regardless of message origin.

`Starrable<T>` encodes that some metadata fields carry a wildcard (`*`) meaning "any value" in rule table matching, rather than relying on magic strings.

## What Is Not Yet Built

- ☐ `HtgFileImportedSubscriber` — legacy HTG-internal Kafka subscriber is commented out; not yet migrated to the consolidated handler pattern
- ☐ Rate expression coverage — `IRateExpressionMapper` supports `fixRate(n)` and `rateDividedBy(n)`; unmapped expressions fall through silently with no warning
- ☐ Unstructured deduction-to-reimbursement end-to-end — `DeductionMappingUnstructured` mirrors AI logic but the `FileExtractedHandler` path has limited end-to-end test coverage
- ☐ Batch date validation — no equivalent of a `ResolveBatchWeekEndingDate` fallback guard; `ArrivalDateAndTime` metadata is used as-is for batch date

## Key Design Decisions

| Decision | Rationale |
| --- | --- |
| SQS is a relay to Kafka, not a processor | Decouples StudioPlus event delivery from processing. Kafka gives replay, backpressure, and multi-consumer routing that SQS alone does not. |
| Template-driven mapping via rule tables | No code changes needed to add or modify a field mapping — templates are configured in the rule table UI. |
| Ordered `ITimecardDistributionMapping` pipeline | Mappers are independent and composable. Adding a new transformation = new class + registration; no existing mappers change. |
| `IDeductionDistributionMapping` keyed by `ExecutionType` | Avoids branching in shared mapping code. Each execution type owns its full deduction behavior. |
| File tracking is log-based | Avoids an extra database dependency and schema migration. Dashboard queries use structured log aggregations (Elasticsearch). |
| AI raw response stored in Redis | AI payloads can be large; storing out-of-band keeps Kafka message payload small and allows re-processing without re-calling the AI service. |
| Arrival methods cached 24 hours in Redis | List changes rarely; caching avoids an ISS API call on every message. TTL is short enough to pick up changes same day. |
| `FileExtractedHandler` feature-flagged | Unstructured/AICOE flow is independently deployable. Disabling the flag ignores `FileExtracted` messages without a redeploy. |
| `Starrable<T>` for metadata fields | Some fields carry `*` (wildcard) in rule table matching. `Starrable<T>` encodes that intent in the type rather than with magic strings. |
| Assembly scanning via Scrutor | Auto-discovers all mapper implementations. Adding a mapper class is sufficient — no manual `services.AddScoped<ITimecardDistributionMapping, NewMapper>()` needed. |
| `InternalExtractionResponseMapper` bridges Unstructured → AI shape | Reuses `AIDataIngestion.ExtractionResponseMapper` without duplicating it. Unstructured is a delivery variant of AI, not a fundamentally different format. |
