# Onboarding Package Linkage, Counts and Retry — Design

Owner / designer: Christian Oporto. Executing squad: Silverwing.
Epic: Onboarding Documents (Employee, Deal Memo, Corporation) via AI into HTG, Phase 2.

Scope: how an onboarding package is split, saved, counted and retried, and how every saved record is linked to its package.
The tracker's retry button and the send to Payroll are separate designs.

Build plan, ticket inventory and discussion: [Confluence page](https://castandcrew.atlassian.net/wiki/spaces/HPS/pages/5654708292).

---

## Summary

An "onboarding package" is one uploaded file of employee forms. AI extraction turns it into employees, deal memos and, for
loan-out employees, corporations, and HTG saves them. This design links every saved record to the package it came from,
counts the records of each package, keeps the newest upload of an employee as the winner, and retries a failed employee alone.

**Why.** Nothing links a saved record to its package, so the dashboard cannot show a package's result and a user cannot open the
file a record came from. When more than one import instance runs, two uploads of the same employee can be processed at the same
time and the older one can finish last and overwrite the newer. One failed employee sends the whole package again, a failure
that retrying cannot fix is sent five times, and an employee that runs out of attempts leaves one log line and no way to see or
replay it.

**The design**
- **One message per employee.** The mapper splits each package and keys every message by a keyed hash of the company code and
  the SSN, so all uploads of one employee are processed in order, also with many instances.
- **Upload time decides.** Each message carries the Kafka time of its package. An Employee or Corporation is overwritten only by
  an upload that is not older than the one that wrote it. Every upload creates its own Deal Memo.
- **One row per employee per package** records the documents created. The same row makes a repeated message harmless, and
  counting the rows gives the package's totals when the package is exported to search. The package table gets no new column.
- **Each record stores its package** in an indexed column, so the screens show the package and offer the uploaded file. Employee
  also gets the request id and status columns that Deal Memo and Corporation already have.
- **Retry is per employee,** only for temporary errors, up to 5 times.
- **Every failure for good is a tracker entry** with its package, reference and place in the topic.

**What it gives the business.** The dashboard shows each package's result. A user opens the file behind any record. The last
upload of an employee wins. A failure of one employee does not repeat the others, and no failure disappears.

**What we need from others.**
- The package topic's Kafka message timestamp is the time the producer created the record (creation time, not append time).
- Retention of the package and employee onboarding topics raised from 7 to 14 days.
- The database changes of section 4 applied before the deploy, including a unique constraint on the corporation tax id, after
  existing duplicates are cleared.

**Risks we accept.**
- "Last upload" means the last package record produced upstream, not the last file the user chose. If extraction finishes in a
  different order than the uploads, an older file wins. The user sees older data and uploads again.
- A retry from the tracker works for 14 days. After that the client uploads again.
- If the hashing secret changes, order across the change is not guaranteed; the upload time rule still keeps the newest data.

**Scope.** Mapper split, import save, counts, package columns, retry and failure entries.

---

## 1. Problem

| Behavior | Consequence |
|---|---|
| The mapper publishes one message per package, keyed by the package's Kafka key (`OnboardingPackageHandler`). | Two packages with the same employee can sit on different partitions and run at once on different instances. |
| `Employee_Update` is `UPDATE ... WHERE Id = @Id` and rewrites all custom fields; the provider loads the employee through the Redis cache. | The older upload can finish last and overwrite the newer, and a cached read can be older than the stored row. |
| `OnboardingProcessedReference` is keyed by `ReferenceId` alone and stores only the package id and the date. | A repeated reference id in another package is skipped. No row says which documents a package created, so nothing can count or open them. |
| A committed onboarding set does not request a search refresh of its package, and `IngestionPackages` has no result columns. | The package shows no totals. |
| `OnboardingRetryCoordinator` re-queues the whole message when any set fails, up to 5 times, for any error. | One failed employee sends the package again. An error that retrying cannot fix is sent 5 times. |
| A failed requeue leaves one `Error requeuing` log line, and the last failed attempt one `[DLQ]` log line. | The tracker does not show the failure and nothing can replay it. |
| `Employee_Insert` and `Employee_Update` have no `RequestId` or `Status`; `DealMemo_Insert` and `Corporation_Insert` have both. | An Employee cannot be sent to Payroll the way the other two documents are. |

## 2. Goals and non-goals

Goals
- Every record created from a package stores that package, and the package's totals are always the real number of employees processed.
- The last upload of an employee wins, with several instances and for a message that is being retried.
- A retry never creates a duplicate and never repeats an employee that is already saved.
- Every failure for good is visible in the tracker with enough data to replay it.

Non-goals
- The tracker's retry button and the reader that fetches a message by offset (own design).
- The send of Employee, Deal Memo and Corporation to Payroll (own design).
- What the importer stores in the Employee, Deal Memo and Corporation fields.
- A delete flow: onboarding never deletes.

## 3. Architecture

```
AiCOE package record  (Kafka timestamp T)
        │
        ▼
ExternalDataMapper ── one message per employee, key = HMAC(dbCode, SSN), packageTimestamp = T
        │
        ▼
OutOnboardingSaved ───────────► Onboarding Import (one message at a time per consumer)
        ▲                          │ one transaction per employee:
        │ retry, same key          │   lock, fresh read, save Corporation, Employee, Deal Memo,
internal retry topic ◄─────────────┤   write one OnboardingProcessedReference row
                                   │ after the commit: ask search to refresh the package
                                   ▼
          SQL: employee, DealMemo, Corporation, OnboardingProcessedReference
                                   │
                                   ▼
          ElasticExporter: ingestionpackages document + counts from the reference rows
```

- **ExternalDataMapper** splits, keys and stamps. It does not know whether two packages hold the same employee.
- **Onboarding Import** saves one employee in one transaction and decides the retry.
- **OnboardingProcessedReference** remembers what each employee of each package created.
- **ElasticExporter** counts those rows when it builds the package document.
- **The tracker** shows the failures. It does not decide anything.

## 4. Components

### 4.1 Mapper: one message per employee

- Each entry of `onboardingSets` becomes its own message: the package metadata at the root, exactly one set, and the number of
  employees of the package. The contract is the current one, so Onboarding Import needs no model change for the split.
- The key is an HMAC of the company code and the SSN digits with a secret kept in configuration. A plain hash of nine digits can
  be reversed by brute force. One employee always lands on one partition and is processed in the order it was produced
  (see References).
- Every message carries `packageTimestamp`, the Kafka timestamp of the package record. The router captures it on the first
  attempt and keeps it in the envelope, so a mapper retry does not change it. The handler receives it as a parameter.
- If the mapper fails after producing some messages, its retry produces them all again with the same `packageTimestamp`; the
  reference row (4.2) makes the repeats harmless.

Trade-offs: N messages per package instead of one, each repeating the package metadata. Ordering is by production order, which
is why the upload time rule (4.3) exists.

### 4.2 Reference table

One row per employee per package, written in the transaction that saves the employee.

| Column | Type | Notes |
|---|---|---|
| `PackageId` | `NVARCHAR(200) NOT NULL` | Package GUID as text. Part of the key. |
| `ReferenceId` | `NVARCHAR(200) NOT NULL` | From AiCOE. Part of the key. |
| `EmployeeId` | `UNIQUEIDENTIFIER NULL` | Employee document id. |
| `DealMemoId` | `UNIQUEIDENTIFIER NULL` | Deal Memo document id. |
| `CorporationId` | `UNIQUEIDENTIFIER NULL` | Null when the employee has no corporation. |
| `ProcessedDate` | `DATETIME2 NOT NULL` | Default `SYSUTCDATETIME()`. |

- Primary key (`PackageId`, `ReferenceId`): the same reference id in another package never collides, and package lookups are
  index seeks. An index on (`EmployeeId`, `ProcessedDate`) answers how many times an employee was uploaded.
- The parser skips a message whose key exists (an Idempotent Receiver, see References). An employee uploaded again in a new
  package is processed.
- Rows are never deleted: the counts depend on them. Existing rows keep their package id and get null document ids.
- One idempotent script applies the change before the deploy.

Trade-offs: the table grows by one small row per employee processed, forever.

### 4.3 Order of uploads

The same rule runs for a message from the main topic and for one from the retry topic.

1. Inside the employee's transaction, before writing the Corporation and before writing the Employee, take a transaction-scoped
   application lock on the document key (`sp_getapplock`): the tax id for the Corporation, the employee key for the Employee.
   Locks are taken in the materializer's order, Corporation first, and released at commit.
2. Read the document from SQL, not through the cache.
3. If the stored `sourceTs` is later than `packageTimestamp`, do not overwrite the document. Otherwise write it with
   `packageTimestamp` as its `sourceTs` and the message's package as its package id. A document without `sourceTs`, or with an
   equal one, is written.
4. When the Employee is not overwritten, its subrecords are not written either: they are always inserted new, and the old ones
   would sit next to the newer ones.
5. The Deal Memo has no rule. Every upload creates its own Deal Memo, with its own package id.
6. The employee is marked processed in the reference table with the ids of the existing documents. A skipped write is logged as
   superseded with package id, reference id and both timestamps.

`sourceTs` is a date custom field, set only by Onboarding Import, on Employee and Corporation. It has its own name and does not
reuse the `updatedTs` that Payroll fills on Corporation.

Trade-offs: employees of the same corporation, or two uploads of one employee, wait for each other on the lock. An older upload
never changes an Employee or Corporation, even in a field the newer upload lacks.

### 4.4 Columns on the documents

- `PackageTrackingId` (`UNIQUEIDENTIFIER NULL`, indexed) on `employee`, `DealMemo` and `Corporation`. It holds
  `IngestionPackages.Id`, the package GUID, not the numeric `PackageTrackingId` identity of that table. It is set only by
  Onboarding Import and exported to Elasticsearch as a plain string field.

  | Document | Value |
  |---|---|
  | Employee | The latest upload that wrote it (the key is dbCode and SSN). |
  | Deal Memo | Its own package. An employee uploaded in package 1 and again in package 2 has two Deal Memos; the first must keep package 1. |
  | Corporation | The latest upload that wrote it (shared by tax id). |

- `RequestId` (`UNIQUEIDENTIFIER NULL`) and `Status` (`TINYINT`, the `DocumentStatus` values) on `employee`, as Deal Memo and
  Corporation have. Existing employees read as Processed with no request id; an employee created by onboarding starts as
  Pending, and the send to Payroll assigns the request id.
- Each column is carried by the insert, update and get-by-id procedures and by the entity. A get-by-id that does not return a
  column breaks `Store()` after a load by id. A flow that does not set a column keeps the stored value.
- Records created before the change have no package id and no `sourceTs`; the screens show nothing for them, and the first
  upload that reaches them writes.

### 4.5 Counts and package search

- After each employee's commit, Onboarding Import requests a refresh of the package by producing to
  `ingestionpackages-record-updated`, with the after-commit mechanism the set-materialized trigger uses.
- `ExportIngestionPackages` counts the reference table for the package, asynchronously through `ObjectCache.ExecuteAsync`, and
  adds `employeeCount` (rows), `dealMemoCount` (rows with a `DealMemoId`) and `corporationCount` (distinct `CorporationId`) to the
  document, only when above zero. `IngestionPackages` gets no column, procedure or entity change.
- `CreateIngestionPackagesSchema` applies the mapping when the index already exists, so the new fields are searchable without
  recreating it.

Trade-offs: a package of N employees causes N exports, and each rebuilds the whole document. Gain: the count is always the
rows written with each employee, also after retries.

### 4.6 Screens: package and download

1. The screen reads `PackageTrackingId` from the document.
2. A read endpoint returns the package (name, file key, bucket) for a package id.
3. The screen shows the package name and downloads the uploaded file through the existing pre-signed S3 link.

A record without a package id, or whose package has no row, shows nothing.

### 4.7 Retry

| Situation | Behavior |
|---|---|
| Invalid JSON or no sets | Error log, message dropped. |
| Temporary error | The same employee message goes to the internal retry topic, same key, attempt + 1, up to 5 attempts. |
| Error that retrying cannot fix | No retry; failure entry (4.8). |
| Attempts exhausted, or the retry cannot be produced | Failure entry (4.8). |
| Delivered twice, or retried after it committed | The reference row skips it. |

- Temporary: SQL timeout, deadlock (1205) and connection failure; unique-key violation (2601, 2627) on the corporation upsert
  (4.9); Kafka produce and transport exceptions; `TimeoutException`. One classifier with unit tests decides. Anything else is
  permanent.
- A retry runs on the dedicated retry consumer, concurrently with the main one. A retried older upload is safe because of the
  rule in 4.3.
- No delay between attempts, the same as the other processors.

Trade-offs: an outage longer than a few seconds exhausts the 5 attempts and the employee becomes a tracker entry, which the
tracker retries within the retention window.

### 4.8 Failures in the tracker

Every employee that fails for good, or runs out of attempts, is one tracker entry written through the process logs library, the
same one the tracker's other events use, so a failure is recorded once. The entry carries data, not only text:

- package id and reference id;
- the step where it stopped (mapper, Onboarding Import, Payroll);
- the reason and the attempt count;
- topic, partition and offset of the message, and its timestamp.

The entry never carries the payload, which holds the SSN. The retry button reads the message back by topic, partition and offset
and resumes from the recorded step: an employee already saved is not saved again, because the reference row holds its document ids.
That button and the reader are their own design.

### 4.9 Corporation under several instances

Employees with different SSNs can reach different instances. If they belong to the same new corporation, both try to create it.
The `Corporation` table has a unique constraint on `ExternalId` (the tax id). The one that loses gets a unique-key violation,
which is a temporary error (4.7): its retry finds the corporation and updates it. The lock of 4.3 serializes sets of the same
corporation that reach the database at the same time.

## 5. Failure modes

| Failure | What happens | Recovery |
|---|---|---|
| Temporary error on an employee | Rollback, retry of that employee only | Automatic |
| Permanent error, or 5 attempts exhausted | Failure entry in the tracker | Fix, then retry from the tracker |
| Retry cannot be produced | Failure entry in the tracker | Retry from the tracker |
| Delivered twice, or retried after commit | Skipped by the reference row | Automatic |
| Shutdown in the middle of a message | Offset not stored, redelivered; skipped if it had committed | Automatic |
| Older upload, retried or concurrent | Document not overwritten, Deal Memo still created, logged as superseded | Automatic |
| Mapper fails after producing some employees | Its retry produces all again with the same timestamp; reference rows make it harmless | Automatic |
| Two employees create the same corporation | One gets a unique-key violation and retries | Automatic |
| Package refresh cannot be produced after commit | Logged; the next employee or export refreshes the counts | Automatic |
| Package without an `IngestionPackages` row | Records are saved; screens show no package | None needed |

## 6. Scale

- Order follows the key: one employee is processed one message after another. Different employees are not ordered and do not need to be.
- Concurrency is partitions times instances; each consumer handles one message at a time.
- The retry consumer runs beside the main one on every instance. Locks serialize only the same employee and the same corporation.
- Counting is an index seek on the reference table's key.

## 7. Observability

- Log prefix `[OnboardingImport]`. The scope of each message carries `PackageId`, `ReferenceId`, `AttemptCount`, `Topic`,
  `Partition` and `Offset`, so a failure is found from the log line alone.
- The SSN is masked to its last four digits in every log line. The payload is never logged, and the key contains no SSN.
- Logs to watch: `Superseded` (with both timestamps), `Requeued`, and failure entries (alert on any).
- After a deploy: the package totals equal the employees processed, and a day of traffic has no failure entry.

## 8. Decisions and prerequisites

No open decisions. Prerequisites outside the code:
- The package topic's Kafka message timestamp is the producer's creation time.
- The hashing secret for the employee key is kept in configuration secrets.
- Retention of the package and employee onboarding topics is 14 days.
- Database scripts applied before the deploy: the reference table, the three document tables (column and index), the Employee
  `RequestId` and `Status`, and the unique constraint on the corporation tax id after duplicates are cleared.

## 9. Out of scope

- The tracker's retry button and the reader that fetches a message by topic, partition and offset.
- The send to Payroll.
- The Onboarding Dashboard screens themselves.

## References

- [Apache Kafka, Introduction](https://kafka.apache.org/intro): events with the same key are written to the same partition,
  and a consumer reads a partition's events in the order they were written. Used for the employee key (4.1).
- [Idempotent Receiver, Enterprise Integration Patterns](https://www.enterpriseintegrationpatterns.com/patterns/messaging/IdempotentReceiver.html):
  a receiver that can safely receive the same message several times. Used for the reference row (4.2).
