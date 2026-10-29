# Onboarding Import — Package Linkage, Counts and Retry Design

Source: Confluence page 5654708292, space HPS, last modified Oct 21, 2026.

**Status:** Draft for review.
**Date:** 2026-10-21
**Repository copy:** `docs/onboarding/DESIGN_OnboardingPackageLinkage.md` (PR 19448)
**Related:** H2G-7016 (epic, Onboarding Documents Phase 2), H2G-6967 (Onboarding Dashboard), H2G-7185 (package metadata and logs), H2G-7147 (IPCP package intake), H2G-7184 (package metadata in the onboarding message), Onboarding Import Service - Technical Design, Onboarding Import - File Tracking and AI Data Visibility, Onboarding Kafka Message Contract (H2G-6389 Onboarding Kafka Message Contract for Employee and Deal Memo Sets)

---

## 1. Purpose and summary

The Onboarding Dashboard must show, for each ingestion package, how many Employee, Deal Memo and Corporation records were processed and let the user open each one. The Employee, Deal Memo and Corporation screens must show the package a record came from and let the user download the uploaded file. Onboarding Import has to scale to several pods, keep the last upload of an employee as the winner, retry safely and show every failure in the tracker.

The design in one paragraph: ExternalDataMapper splits each package into **one message per employee**, keyed by a hash of dbCode and SSN, and stamps each message with the **Kafka timestamp of the package record**. Onboarding Import records **one row per employee per package** in a restructured `OnboardingProcessedReference` table, written in the same transaction as the employee. An Employee or Corporation is overwritten only by an upload that is not older than the one that last wrote it, judged by that timestamp. The ElasticExporter **counts the table** when it builds the `ingestionpackages` document, so `IngestionPackages` gets no new columns. The package id is stored in an **indexed column** on the Employee, Deal Memo and Corporation tables, and Employee also gets the request id and status columns it needs to be sent to Payroll. A failed employee is retried alone, and every employee that fails for good is visible in the tracker.

## 2. Requirements

| # | Requirement | Source |
| --- | --- | --- |
| R1 | Per-package counts of Employee, Deal Memo and Corporation records, usable in the dashboard rollups (for example "38 DM, 112 Emp, 12 Corp"). | H2G-6967, Remaining Work section 8 |
| R2 | From a package, open the profile of each Employee, Deal Memo and Corporation created from it. | Dashboard requirement |
| R3 | Query Employee documents by package id. | Dashboard requirement |
| R4 | A retry must not create duplicates. A failed employee is retried alone, not the whole package. | Retry model |
| R5 | When an employee is uploaded again in a new package, the last upload wins, also with several import pods and also for a message that is being retried. | Scaling requirement |
| R6 | Every employee that fails for good or runs out of attempts is visible in the tracker with package id, reference id and reason, and can be retried from there. | Tracker requirement |
| R7 | On the Employee, Deal Memo and Corporation screens the user sees the package the record came from and downloads the uploaded file. | Screen requirement |
| R8 | The business can ask how many times an employee was uploaded. | Business question |
| R9 | The dashboard lists active documents with a 30-day default window and reuses the Batches-style grid. | H2G-6967 |

## 3. Current state (evidence)

| # | Fact | Where |
| --- | --- | --- |
| 1 | The `IngestionPackages` row is created by ExternalDataMapper when IPCP sends the package, before AiCOE and before onboarding. Its `Id` is the package GUID, which comes back as `OnboardingMessage.PackageId`. | `IpcpPackageIntakeService.cs:96-104`, `OnboardingPackageMapper.cs:47,106` |
| 2 | ExternalDataMapper produces **one message per package** to `OutOnboardingSaved`, keyed by the package's Kafka key. | `OnboardingPackageHandler.cs:56` |
| 3 | `OnboardingProcessedReference` has `ReferenceId` as primary key, plus `PackageId` and `ProcessedDate`. The parser skips any set whose `ReferenceId` already exists, whatever its package. The materializer writes the row in the same transaction as the set. | `OnboardingParser.cs:73`, `OnboardingMaterializer.cs:92-107`, `OnboardingProcessedReference.sql` |
| 4 | The three document ids of a set (`employeeId`, `dealMemoId`, `corporationId`) exist in the materializer when the set is marked processed. | `OnboardingMaterializer.cs:100-107` |
| 5 | Each pod runs two consumers: one for the onboarding topic and a dedicated one for the internal retry topic. Each processes its messages one at a time. Several pods share the partitions of a topic. | `OnboardingIngestionRegistration.cs:53-56`, `DedicatedTopicConsumer.cs` |
| 6 | When a handler throws, the consumer logs `[DLQ] Message dropped` and continues. The offset is stored only after a successful handler. Only a graceful shutdown in the middle of a message leaves the offset unstored, so that message is delivered again on restart. | `MultiTopicKafkaConsumer.cs:73-85`, `DedicatedTopicConsumer.cs:122-125` |
| 7 | The coordinator re-queues the whole message when any set fails or an exception occurs (5 attempts). It does not tell transient errors from deterministic ones. If producing the retry fails, the failure is logged and nothing is retried. A set that exhausts its attempts leaves a `[DLQ]` log line only. | `OnboardingRetryCoordinator.cs:30,75-81,92-98,118-123` |
| 8 | Employee is upserted by dbCode and SSN. Corporation is upserted by tax id. Deal Memo is always inserted with a new GUID. Employee subrecords are always inserted new. | `OnboardingEmployeeProvider.cs:62-68`, `OnboardingDealMemoProvider.cs:41-50` |
| 9 | The contract defines `referenceId` as unique per employee and deal memo set **within the document**, stable across retries (the same message delivered again carries the same values). It does not say whether it is unique across packages or what a re-upload receives. | Contract page |
| 10 | `ExportIngestionPackages` rebuilds the whole `ingestionpackages` document from the database on every export. Exports are requested explicitly, by producing to `ingestionpackages-record-updated`, on package creation, status change and assignment. A committed onboarding set requests none. The DataChanges poller reads `dbo.Documents`, not `IngestionPackages`. | `IngestionPackagesExporter.cs:241-322`, `IngestionService.cs:139,163,177,190`, `Documents_ChangeQuery.sql` |
| 11 | `CreateIngestionPackagesSchema` returns when the index exists, so new fields are not mapped on an existing `ingestionpackages` index. Document indexes (Employee and others) re-apply their mapping when the exporter starts. | `IngestionPackagesExporter.cs:169-170`, `ExportDocumentDataAsync.cs:929-949` |
| 12 | `Corporation_Insert` is a plain `INSERT` with no existence check. The DDL of the `Corporation` table is not in source control, so the unique constraint on `ExternalId` cannot be confirmed from the repository. Other tables have one (Invoice, PayrollAgreement). | `dbo_Corporation_Insert.sql` |
| 13 | The `IngestionPackages` row holds `FileKey` and `BucketName`, and the Elasticsearch document carries `ingestionPackagesId`, `packageTrackingId`, `fileKey` and `bucketName`. Ingestion.Web.API returns a pre-signed S3 URL for a package file, by numeric tracking id (`GET getDocumentFile/{packageTrackingId}`) or by file key and bucket (`POST getDocumentFile`). Nothing resolves a package by its GUID. | `IngestionController.cs:231-250`, `IngestionPackagesExporter.cs:273-275` |
| 14 | Process File Tracking is event-sourced from log lines in Elasticsearch, with no durable table and a 7-day default window. | File Tracking page |
| 15 | The dashboard lists active documents, default window 30 days. | H2G-6967, Remaining Work section 8 |
| 16 | H2G-7185 delivered the package metadata in the onboarding message and in the logs. The link from a set to its package and documents is defined in this design. | H2G-7185 |
| 17 | The mapper's router receives the Kafka record with its timestamp but does not pass it on: the handler gets only the raw data and the key. When a package fails, the router produces the envelope again to the same topic, so the new record carries the time of the retry. The envelope has no field for the original timestamp. | `ExternalDataRouter.cs`, `IExternalDataHandler.cs`, `ExternalDataEnvelope.cs` |
| 18 | `Employee_Update` is an unconditional `UPDATE ... WHERE Id = @Id` that rewrites the whole `CustomFields`. The provider loads the employee through `GetDocByExternalIdAsync`, which reads through the Redis cache. | `dbo.Employee_Update.sql`, `Document.cs:419-424` |
| 19 | The process logs library that writes tracker events is H2G-7173 (HTGPlus.ProcessLogs), Ready to Deploy on 2026-10-20. H2G-7175 (mapper) and H2G-7177 (Onboarding Import, per onboarding set) emit tracking events for the same tracker and are Ready For Dev. | Jira |
| 20 | Employee has no `RequestId` or `Status` column: `Employee_Insert` and `Employee_Update` have neither, while `DealMemo_Insert` and `Corporation_Insert` carry both (`DocumentStatus`: Pending, Processed, InTransit, AwaitingPayroll). H2G-7157 generates `htgRequestId` in the UI and leaves out storing it and Payroll's echo. | `dbo.Employee_Insert.sql`, `dbo.DealMemo_Insert.sql`, `dbo_Corporation_Insert.sql`, `ModuleStatus.cs`, H2G-7157 |
| 21 | A column that the get-by-id procedure does not return breaks `Store()` after a load by id: `Employee_GetById` did not return `DbCode`. | H2G-7145 |

## 4. Design

### 4.1 One message per employee

ExternalDataMapper's `OnboardingPackageHandler` produces one message for each entry of `onboardingSets` instead of one for the package (fact 2).

- **Shape.** The message keeps the current contract: the package metadata at the root and `onboardingSets` with exactly one entry. Onboarding Import needs no model change for the split. Each message also carries the number of employees of the package, so completion can be derived later.
- **Key.** An HMAC of dbCode and SSN digits with a secret, not a plain hash: a 9-digit SSN can be recovered from a plain hash by brute force. Every upload of the same employee goes to the same partition, so with several pods the messages are processed in the order they were produced.
- **Why.** With the package as the key, two packages for the same employee can sit on different partitions and run at the same time on different pods (facts 2 and 5).
- **Package timestamp.** The key keeps the production order, but the mapper receives each package whole and cannot know that two packages hold the same employee, so the order of production is not the order of upload. Every message therefore carries `packageTimestamp`, the Kafka timestamp of the package record, and the save rule of section 4.6 uses it. The router captures the timestamp on the first attempt and keeps it in the envelope, so a retry does not change it (fact 17). The handler receives it as a parameter.
- **Failure while producing.** If the mapper fails after producing some messages, its own retry produces them all again, with the same `packageTimestamp`. The marker in section 4.2 makes the repeats harmless.

### 4.2 Reference table: `OnboardingProcessedReference` restructured

One row per employee per package, written in the same transaction that saves the employee.

| Column | Type | Notes |
| --- | --- | --- |
| `PackageId` | `NVARCHAR(200) NOT NULL` | Package GUID as text. Part of the key. |
| `ReferenceId` | `NVARCHAR(200) NOT NULL` | From AiCOE. Part of the key. |
| `EmployeeId` | `UNIQUEIDENTIFIER NULL` | Employee document id. Required for new rows. |
| `DealMemoId` | `UNIQUEIDENTIFIER NULL` | Deal Memo document id. Required for new rows. |
| `CorporationId` | `UNIQUEIDENTIFIER NULL` | Null for sets without a corporation. |
| `ProcessedDate` | `DATETIME2 NOT NULL` | Default `SYSUTCDATETIME()`. |

- **Key:** primary key on (`PackageId`, `ReferenceId`), so a repeated `ReferenceId` in another package cannot collide (fact 9). The key leads with `PackageId`, so package lookups and the counts of section 4.3 are index seeks.
- **Index:** `EmployeeId` with `ProcessedDate`. "How many times was this employee uploaded" is `COUNT(*) WHERE EmployeeId = @id` (R8).
- **Idempotency:** the parser skips a message whose (`PackageId`, `ReferenceId`) exists. An employee uploaded again in a new package is processed, which is the intended behavior (R5).
- **Retention:** rows are never purged, because the counts depend on them. Each row is small.
- **Migration:** one idempotent script under `SQL/SourceControl/OneTime`, applied before the deploy: add the columns, add the index, replace the primary key. Existing rows keep their package id (an empty string when it was missing) and null document ids.
- **Code:** `MarkProcessedAsync` receives the package id and the three document ids (fact 4). `IsProcessedAsync` takes both key parts.

### 4.3 Counts at export

`IngestionPackages` gets no new columns, no stored procedure and no entity change. `ExportIngestionPackages` counts the reference table when it builds the document.

| Elasticsearch field | Count |
| --- | --- |
| `employeeCount` | rows of the package |
| `dealMemoCount` | rows of the package with a `DealMemoId` (one per row) |
| `corporationCount` | distinct `CorporationId` of the package |

- The query is asynchronous, through `ObjectCache.ExecuteAsync` like the onboarding store, not through the synchronous `IngestionPackage.Get`.
- The fields are added to the document only when a count is above zero, so timecard packages stay as they are.
- A count cannot drift from the real data, because it comes from the rows written with each employee, also after retries.
- **Export request.** A committed set does not request an export today (fact 10). Onboarding Import produces to `ingestionpackages-record-updated` after each commit, with the same after-commit mechanism used for the set-materialized trigger. A package of N employees causes N exports; each rebuilds the document.
- **Mapping.** The new fields are mapped on the existing index: `CreateIngestionPackagesSchema` applies the mapping even when the index exists (fact 11).

### 4.4 Package id and upload timestamp on the documents

The package id is stored in a column `PackageTrackingId` (`UNIQUEIDENTIFIER NULL`, indexed) on the `employee`, `DealMemo` and `Corporation` tables. It holds the id of the ingestion package (`IngestionPackages.Id`, the package GUID), not the numeric `PackageTrackingId` identity of that table. Each table gets the column, the index, the insert, update and get-by-id procedures that carry it (a procedure that does not return it breaks `Store()` after a load by id, fact 21) and the `[Field]` on its entity. The entity property is exported to Elasticsearch as a plain string field. The column is set only by Onboarding Import and is never supplied by an external source.

`sourceTs` is a date custom field, HTG-only, on Employee and Corporation. It holds the `packageTimestamp` of the upload that last wrote the document. It is a custom field, so it needs no migration. `sourceTs` has its own name and does not reuse the `updatedTs` field that Payroll fills on Corporation.

| Document | `PackageTrackingId` (column) | `sourceTs` (custom field) |
| --- | --- | --- |
| Employee | The latest upload that wrote it (the key is dbCode and SSN). | Timestamp of that upload. |
| Deal Memo | Its own package. Every upload creates a new Deal Memo, so it must not take the value from the employee: an employee uploaded in package 1 and again in package 2 has two Deal Memos, and the one from package 1 would otherwise show package 2. | None. |
| Corporation | The latest upload that wrote it (shared by tax id). | Timestamp of that upload. |

`PackageTrackingId` and `sourceTs` are written together, and only when the save rule of section 4.6 lets the write through. A Deal Memo always writes its own.

The dashboard finds the documents of a package by the indexed column in SQL, or by the exported field in Elasticsearch (R3). The exact documents of a package, including older ones, are the rows of section 4.2 (R2). The migration is one idempotent script for the three tables (columns and indexes), applied before the deploy. Documents created before this change have no package id or `sourceTs`; the screens show nothing for them and the first upload that reaches them writes.

### 4.5 Package and download on the screens

1. The screen reads the package id (`PackageTrackingId`) from the document.
2. It looks up the package: the `ingestionpackages` index by `ingestionPackagesId` (existing search provider), or `IngestionPackage.GetById`.
3. It shows the package name and builds the download from `fileKey` and `bucketName` of that package, through `POST getDocumentFile` (fact 13).

The existing `GET getDocumentFile/{packageTrackingId}` takes the numeric id, so either the screen uses the lookup of step 2 or an endpoint by package GUID is added. The file is a pre-signed S3 URL; whether it is the same store as the Studio+ file key is to be confirmed (section 8).

### 4.6 Retry model and order of uploads

**How it works today** (facts 5, 6 and 7). The coordinator re-queues the whole message on any failure, does not tell transient from deterministic errors, and relies on the reference table to skip committed sets. A failed re-queue or an exhausted set leaves only a log line. A retry runs on the dedicated consumer of the internal topic, concurrently with the main consumer.

**How it should work.** With one message per employee the unit of retry is the employee message. There is one path, not two.

| Situation | Behavior |
| --- | --- |
| Parse fails (invalid JSON, no sets) | Error log, message dropped. |
| Transient error while processing | Publish the same employee message to the internal retry topic, same key, attempt + 1. |
| Permanent error | No retry. Record the failure (section 4.7). |
| Attempts exhausted (5) | Record the failure (section 4.7). |
| Publishing the retry fails | The consumer drops the message and logs it (fact 6). The failure is recorded (section 4.7). |
| Graceful shutdown in the middle of a message | The message is delivered again on restart. The reference table skips it if it had committed. |
| Delivered twice, or retried after it committed | The reference table skips it. |

**Transient errors.** One classifier, with unit tests. Initial list: SQL timeouts, deadlocks (1205) and connection failures; unique-key violations (2601, 2627) on the corporation upsert (section 4.8); Kafka produce and transport exceptions; `TimeoutException`. Anything else is permanent.

**Order of uploads.** One rule, the same for a message from the main topic and for one from the retry topic, so a retried older upload cannot overwrite a newer one.

1. The message carries `packageTimestamp` (section 4.1). The documents carry `sourceTs` (section 4.4).
2. Inside the set's transaction, before writing the Corporation and before writing the Employee, the code takes a transaction-scoped application lock on the document key (`sp_getapplock`: the tax id for the Corporation, the employee key for the Employee), then reads the document from SQL, not through the Redis cache, because the cached copy can be older than the committed one. The locks are taken in the materializer's order, Corporation first, so two sets cannot wait for each other. They are released at commit.
3. If the stored `sourceTs` is later than `packageTimestamp`, the upload is older than the one that last wrote the document, and the document is not overwritten. Otherwise the document is written with `packageTimestamp` as its `sourceTs` and the message's package as its package id. A document without `sourceTs`, or with an equal one, is written.
4. When the Employee is not overwritten, its subrecords are not written either: they are always inserted new, and the old ones would sit next to the newer ones.
5. The Deal Memo has no rule: every upload creates its own Deal Memo, so it is always created, with its own package id.
6. The set is marked processed in the reference table with the ids of the existing documents, so it counts. A skipped write is logged as superseded, with package id, reference id and the two timestamps.

**Retry delay (optional).** The retry is produced at once, so a short outage can consume the attempts in seconds. A delay that grows with the attempt number is a separate improvement.

### 4.7 Failures in the tracker

Every employee that fails for good, or runs out of attempts, is written to the DLQ log through the process logs library (HTGPlus.ProcessLogs, H2G-7173), so it appears in the tracker with package id, reference id and reason. The entry carries data, not only text:

- package id and reference id;
- the step where it stopped (mapper, Onboarding Import, Payroll);
- the reason and the attempt count;
- topic, partition and offset of the message, and its timestamp.

**Retry button.** The tracker row has a retry button that sends that employee again as a single-employee message. It resumes from the step recorded in the entry:

| Step where it stopped | What retry does |
| --- | --- |
| Mapper, or before the employee was saved | Process the message again. |
| Saved in Onboarding Import, not yet sent to Payroll | Do not save again (a second Deal Memo would be created). Publish the set-materialized trigger again from the ids in the reference table, which holds `EmployeeId`, `DealMemoId` and `CorporationId`. |
| Every step completed | Nothing. |

- **Data for the first row.** The message is read back from the topic of the step where it stopped, by topic, partition and offset: the AiCOE package topic if it stopped at the mapper, the employee topic if it stopped at Onboarding Import. The platform has no component that reads a message by offset; building one belongs to the tracker work.
- **Retention.** The topics retention is raised from 7 to 14 days. After that the message is gone and the tracker tells the user that retry is no longer possible and the client must upload again. The tracker reader's default window is 7 days (fact 14), so the effective retry window is the shorter of the two.
- **Scope.** The button, the step tracking and the topic reader are a design of their own. This design fixes the data they need: the ids in the reference table and the fields above in the entry.

### 4.8 Corporation under several pods

Two employees with different SSNs can land on different partitions and different pods. If both belong to the same new corporation, both try to create it. The upsert by tax id protects only if the database refuses the second insert.

- The `Corporation` table needs a unique constraint on `ExternalId` (fact 12). It is verified on the real database and added if missing, after checking for existing duplicates.
- A set that hits the violation is a transient failure (section 4.6). Its retry finds the corporation and updates it.
- The application lock of section 4.6 on the tax id also serializes sets of the same corporation that reach the database at the same time.

### 4.9 Employee request id and status

Employee gets two columns, as Deal Memo and Corporation already have (fact 20): `RequestId` (`UNIQUEIDENTIFIER NULL`) and `Status` (`TINYINT`, the `DocumentStatus` values). They let a record be sent to Payroll and Payroll's response be matched to it.

- Existing employees read as Processed, with no request id. An Employee created by Onboarding Import starts as Pending.
- The insert, update and get-by-id procedures and the entity carry both columns (fact 21). A flow that does not set them keeps the stored values.
- Who assigns the request id to an employee created by onboarding is decided with the Payroll send work (section 8).

## 5. Alternatives considered

| Alternative | Why it is not used |
| --- | --- |
| Marker at package level on `IngestionPackages` (counts not null means processed). | A failed package has no marker, so its retry reprocesses committed sets and duplicates Deal Memos and subrecords. |
| Idempotent providers (deterministic ids, reconciled subrecords) with no marker. | Reworks the three providers and depends on `Store()` updating an existing id. The reference table gives the same result and the link. |
| Per-set results in a JSON column on `IngestionPackages`. | Depends on SQL Server JSON functions, cannot be indexed or joined, and answering "how many times was this employee uploaded" means reading every package. |
| Three count columns and an increment stored procedure on `IngestionPackages`. | The export already rebuilds the document each time, so counting the reference table there is always current and touches neither the table, the entity nor the upsert. |
| One transaction for the whole package. | One failing employee would roll back the whole package. |
| Order of uploads by looking up the employee's package id and comparing package creation dates. | Needs an extra read of the package for every employee, and depends on metadata that can be missing. The Kafka timestamp is always present and travels in the message. |
| The package id as a custom field (JSON) on the documents. | A value inside the JSON cannot be indexed, so finding the documents of a package needs Elasticsearch. A column with an index answers it in SQL. |
| Idempotency marker in Redis with an expiry. | Not evaluated. It needs a deeper analysis first: how to keep the marker consistent with the SQL commit of a set, and whether the Redis instance persists data and what eviction policy it uses. The generic Redis helper has no existence check or set-if-absent today. |

## 6. Ticket inventory

All tickets are in project H2G under epic H2G-7016, squad HTG Silverwing, fix version 18.7.0, priority High. Snapshot of 2026-10-21. Jira is the source of truth for status, points and acceptance criteria; this table needs a refresh when tickets move.

| Ticket | Title | Type | Points | Design section |
| --- | --- | --- | --- | --- |
| H2G-7355 | External Data Mapper: send one onboarding message per employee, with the package timestamp | Story | 3 | 4.1 |
| H2G-7356 | Onboarding Import: record one row per employee per package, with the ids of the documents created | Task | 2 | 4.2 |
| H2G-7357 | Onboarding Import: skip processed employees by package and reference id, store the document ids and request the package update | Story | 3 | 4.2, 4.3 |
| H2G-7358 | Onboarding Import: do not overwrite an Employee or Corporation with an older upload | Story | 5 | 4.6 |
| H2G-7359 | Package search: show employee, deal memo and corporation counts for each onboarding package | Story | 3 | 4.3 |
| H2G-7360 | Employee, Deal Memo and Corporation: store the package each record came from | Story | 3 | 4.4 |
| H2G-7361 | Onboarding Import: retry a failed employee alone, only for transient errors | Story | 3 | 4.6, 4.7 |
| H2G-7362 | Corporation: guarantee a single corporation per tax id when employees are processed in parallel | Story | 2 | 4.8 |
| H2G-7363 | Employee, Deal Memo and Corporation screens: show the source package and download the uploaded file | Story | 3 | 4.5 |
| H2G-7364 | Onboarding tracker: show failed employees and let the user retry them (design) | Task | 2 | 4.7 |
| H2G-7365 | AiCOE: confirm where the onboarding reference id comes from | Task | 1 | 8 |
| H2G-7366 | Messaging: raise the retention of the onboarding topics from 7 to 14 days | Task | 1 | 4.7 |
| H2G-7367 | Onboarding Import: keep the package linkage, counts and retry design in the repository | Task | 1 | 1 |
| H2G-7377 | Employee: add the request id and the status so a record can be sent to Payroll | Story | 2 | 4.9 |

## 7. Dependencies

Links in Jira:

| Ticket | Blocked by |
| --- | --- |
| H2G-7357 | H2G-7356 |
| H2G-7358 | H2G-7355, H2G-7360 |
| H2G-7359 | H2G-7356, H2G-7357 |
| H2G-7363 | H2G-7360 |
| H2G-7364 | H2G-7357, H2G-7361 |

Related tickets: H2G-7362 with H2G-7361 (a unique-key violation is retried as a transient error), H2G-7365 with H2G-7357 (the reference id is part of the key), H2G-7366 with H2G-7364 (retention sets the retry window), H2G-7377 with H2G-7157 (the UI generates the request id) and with H2G-7360 (same tables and procedures), H2G-7357 with H2G-7185, H2G-7359 with H2G-6967.

Possible overlap, not linked in Jira: H2G-7361 and H2G-7364 with H2G-7175 and H2G-7177 (tracking events for onboarding packages and sets, section 8).

Order:

- H2G-7356 (table) first, before H2G-7357 and H2G-7359.
- H2G-7355 (split) can ship before the others: a message with one set already works. For the order of uploads, the timestamp part of H2G-7355 and the conditional save of H2G-7358 are needed together, and H2G-7360 provides the package id column and the `sourceTs` field that H2G-7358 uses.
- H2G-7360 is independent of the table; H2G-7363 needs H2G-7360. H2G-7377 touches the same Employee procedures as H2G-7360, so they are best done one after the other.
- H2G-7364 needs the data of H2G-7357 and H2G-7361.
- H2G-7365 and H2G-7366 can be done at any time. H2G-7367 closes the work, once the design is agreed.
- The failure record of H2G-7361 uses the process logs library (H2G-7173) and, for stored issues, the `OnboardingIssue` table of Technical Design section 9 (G2, agreed, not implemented).

## 8. Open questions and pending verification

- **AiCOE** (H2G-7365): is `referenceId` created new for each extraction, or built from the data (for example a hash of the employee content)? Is it unique across packages and not only inside one? If it is built from the data, the same employee uploaded twice with the same content gets the same id, and the second upload would carry a repeated `ReferenceId` (the key of section 4.2 includes the package, so it is still processed).
- **Package record timestamp:** who produces the envelope that reaches `InExternalDataPackage` (it is not produced in this repository), and is its Kafka timestamp the original time of the package (creation time) or the time the broker appended it? Either orders the uploads, but "last upload" then means the last package record produced, not the last file the user uploaded.
- **Corporation** (H2G-7362): does a unique constraint on `ExternalId` exist, and are there duplicates today?
- **Tracker logging and overlap:** the DLQ entries are written with the process logs library (HTGPlus.ProcessLogs, H2G-7173, Ready to Deploy on 2026-10-20). H2G-7175 (mapper) and H2G-7177 (Onboarding Import, per onboarding set) already plan tracking events for the same tracker. The failure record of H2G-7361 and the tracker data of H2G-7364 must be reviewed against those events before implementation, so the same failure is not recorded twice.
- **Employee request id** (H2G-7377): who assigns the request id to an employee created by onboarding (Onboarding Import when it saves, or the Payroll send flow), and whether Pending is the right start status for that flow.
- **File store:** the package file is served as a pre-signed S3 URL; confirm it is the same file as the Studio+ file key.
- **Package row:** whether every onboarding package id has an `IngestionPackages` row (packages not created through IPCP intake).
- **Key secret:** where the HMAC secret of the employee key is stored and rotated.
- **Exports:** the cost of one export per committed employee (N per package) in a real package.
- **Fresh read:** how the conditional save reads the document from SQL without the Redis cache, using the same ORM calls as the provider.
