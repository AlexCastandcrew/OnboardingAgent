# Onboarding Import Service - Technical Design

Source: Confluence page 5422841860, space HPS, last modified oct 09, 2026.

**Ticket:** H2G-6493 (Design) → H2G-6424 (Implementation); package linkage: [H2G-7185](https://castandcrew.atlassian.net/browse/H2G-7185)
**Status:** Roles, identity and issue storage in §2, §7 and §9 are agreed; items in §12 are pending decisions.

### Related documents

- [Onboarding Mapping Service (HTGPlus.ExternalDataMapper) — Technical Design](https://castandcrew.atlassian.net/wiki/spaces/HPS/pages/5558992937/Onboarding+Mapping+Service+HTGPlus.ExternalDataMapper+Technical+Design) — the service that receives the IPCP packages ([H2G-7147](https://castandcrew.atlassian.net/browse/H2G-7147)), maps and validates AiCOE data and produces this service's input. Its §2 carries the same responsibility table and its §13 the same gap register.
- [Onboarding Kafka Message Contract — Employee + DealMemo](https://castandcrew.atlassian.net/wiki/spaces/PSPUAS/pages/5397020675/Onboarding+Kafka+Message+Contract+Employee+DealMemo)

---

## 1. Overview

This service persists onboarding data into HTGPlus. It consumes the onboarding contract ("shape B") from a Kafka topic and stores Corporation, Employee and Deal Memo records (with their subrecords) through the Active Record / ORM path. It does not interpret, translate or validate the data: that is done upstream by `HTGPlus.ExternalDataMapper`, which receives AiCOE's own shape and produces shape B.

The implementation follows the **TimecardProcessor** ingestion pattern: Consumer → Parser → Materializer → Providers (`Document.Store()`). No HTTP endpoints are exposed apart from `/health`.

---

## 2. Responsibility boundary

Agreed roles. The criterion for placing a rule: if it can be computed from the message plus reference data, it belongs in ExternalDataMapper; if it depends on what is already persisted (insert vs. update, a document stored earlier in the same transaction), it belongs in OnboardingImport.

| Concern | ExternalDataMapper | OnboardingImport |
| --- | --- | --- |
| Knowledge of AiCOE's shape and form/field names | Yes | No |
| Field translation and coercion to the HTG schema | Yes | No |
| Field-level validation (dates, numbers, codes, SSN, EIN) | Yes | No |
| Lookup verification (document-typed fields resolve in HTG) | Yes | No |
| Rejecting a set or a package | Yes | No |
| Producing issues (what was wrong, where) | Yes — produces them | Stores them verbatim |
| Receiving IPCP packages, saving the package row, forwarding to AiCOE, replying to IPCP | Yes (IPCP intake module) | No |
| Forwarding ingestion package metadata | Yes — copies it verbatim to the message root | Records and logs it, does not interpret it |
| Document ID assignment | No | Yes |
| Insert-vs-update decision | No (onboarding documents) | Yes |
| Create-time defaults that depend on persisted state | No | Yes |
| Persistence and transactions | No for onboarding documents. The only write is the ingestion package row saved by the IPCP intake module (gap G15). | Yes |
| Idempotency on `referenceId` | Carries it through unchanged | Enforces it |
| Retry/DLQ | Own topic for the mapping flow, infrastructure failures only; IPCP intake never throws and reports failures in its reply | Own topic, infrastructure failures only |

This service fails only for infrastructure reasons (database, Kafka). A message that reaches it is trusted to be well-formed; the pending work to make that true is in §12 (G4, G5, G11).

---

## 3. Architecture

The diagram below is pending a refresh to include ExternalDataMapper upstream and the retry topic; the text in this page is the source of truth until then.

[diagram omitted]

### High-level flow

1. Upstream, `HTGPlus.ExternalDataMapper` receives the package from IPCP, forwards it to AiCOE, receives AiCOE's extraction back and publishes shape B; it arrives on `InOnboardingSaved`.
2. `OnboardingSavedHandler` wraps the message in a first-attempt `OnboardingRetryEnvelope` (`AttemptCount = 0`) and hands it to `OnboardingRetryCoordinator`. `OnboardingInternalHandler` does the same for envelopes coming back from the internal retry topic.
3. The coordinator runs `OnboardingParser` inside a scoped transaction (deserialize, idempotency filter), then `OnboardingMaterializer` outside it.
4. The materializer processes each set in its own transaction: Corporation (if present) → Employee → Deal Memo → mark `referenceId` processed (recording the package and the created document IDs) → queue the set-materialized trigger.
5. On failure the coordinator re-produces the envelope to the internal retry topic (§10).

---

## 4. Project Structure

```
HTGPlus.OnboardingImport/
├── Program.cs                                   ← minimal web host bootstrap
├── Configuration.cs                             ← Serilog→ES, /health, service wiring
├── Ingestion/
│   ├── OnboardingIngestionRegistration.cs       ← UseMulticonsumer + handlers + DI chain
│   ├── OnboardingTopics.cs                      ← InOnboardingSaved, InOnboardingInternal, OutOnboardingInternal
│   ├── OnboardingLog.cs                         ← log template prefix
│   ├── Handlers/
│   │   ├── OnboardingSavedHandler.cs            ← external topic entry point (first attempt)
│   │   └── OnboardingInternalHandler.cs         ← internal retry topic entry point
│   ├── Models/
│   │   ├── OnboardingMessage.cs                 ← packageId, package metadata, onboardingSets (OnboardingSet)
│   │   ├── OnboardingEmployee.cs                ← typed pipeline fields + subrecord lists + CustomFieldData
│   │   ├── OnboardingDealMemo.cs                ← typed pipeline fields + guarantees/allowances + CustomFieldData
│   │   ├── OnboardingCorporation.cs             ← corporation + registrations
│   │   ├── OnboardingRetryEnvelope.cs           ← retry envelope (attempt count, last error)
│   │   ├── OnboardingJson.cs                    ← JSON deserializer (camelCase)
│   │   └── OnboardingResults.cs                 ← parse/materialize results and counts
│   ├── Services/
│   │   ├── OnboardingParser.cs                  ← deserialize + idempotency filter
│   │   ├── OnboardingMaterializer.cs            ← per-set transaction, failure isolation, counts
│   │   ├── OnboardingRetryCoordinator.cs        ← parse + materialize + retry/DLQ decision
│   │   ├── OnboardingIdempotencyStore.cs        ← processed-referenceId store (+ package link)
│   │   └── IHtgScopedTransactionRunner.cs       ← ambient transaction wrapper (testable)
│   ├── Providers/
│   │   ├── OnboardingCorporationProvider.cs     ← corporation upsert + registrations
│   │   ├── OnboardingEmployeeProvider.cs        ← employee upsert + subrecords
│   │   └── OnboardingDealMemoProvider.cs        ← deal memo insert + guarantees/allowances
│   └── Support/
│       ├── SchemaFieldMapper.cs                 ← writes custom fields driven by the document schema
│       ├── LookupIdBuilder.cs                   ← lookup-field ExternalId construction
│       └── Onboarding*Defaults.cs               ← create-time defaults (Employee, DealMemo, Corporation)
├── Support/
│   ├── ProjectRegionLookup.cs                   ← Region from the project document
│   └── SsnMask.cs                               ← dashed SSN format + masking for logs
├── Migrations/                                  ← OnboardingProcessedReference DDL
└── HTGPlus.OnboardingImport.csproj

tests/HTGPlus.OnboardingImport.UnitTests/
```

---

## 5. JSON Contract (Input)

The producer of this contract is `HTGPlus.ExternalDataMapper` (its §5 documents how each field is derived). Original Data Extract contract: [Onboarding Kafka Message Contract](https://castandcrew.atlassian.net/wiki/spaces/PSPUAS/pages/5397020675/Onboarding+Kafka+Message+Contract+Employee+DealMemo).

### Top-level message

```json
{
  "packageId": "ingestion-package-id",
  "packageName": "...",
  "project": "...",
  "uploadedBy": "...",
  "onboardingSets": [ { "...": "..." } ],
  "issues": [ { "...": "..." } ]
}
```

| Field | Notes |
| --- | --- |
| `packageId` | Identifies the ingestion package (the GUID IPCP assigned) in logs and in `OnboardingProcessedReference`. |
| Package metadata | Optional fields at the root, forwarded verbatim by ExternalDataMapper (`packageName`, `project`, `source`, `packageType`, `systemLocation`, `createdDate`, `userTimeZone`, `numberOfDocuments`, `numberOfTimecards`, `documentPageNumbers`, `ipcpReceivedDate`, `ipcpProcessedDate`, `ipcpFileStatus`, `humanIntervention`, `uploadedBy`, `refernacePackageId`, `bucketName`, `fileKey`, `payrollPriority`; the full rules are in the ExternalDataMapper design, §5). This service does not interpret them: it logs the key ones (§10). A message without them, the previous format, keeps working. |
| `onboardingSets` | Sets that passed validation. May be empty when every set of the package was rejected upstream. |
| `issues` | *Target.* Validation issues for all sets, including rejected ones; stored verbatim (§9). Not yet part of the model. |

### Per-set structure

```json
{
  "referenceId": "ONB-2026-000482",
  "corporation": { "...": "..." },
  "employee":    { "...": "..." },
  "dealMemo":    { "...": "..." }
}
```

`referenceId` is the **idempotency key**, provided by AiCOE and carried through unchanged. `corporation` is optional (loan-out employees only); when present it is persisted first because the employee's `taxIdNumber` is a lookup to it.

### Typed properties

Only fields the pipeline's own logic depends on are typed properties on the models. Everything else lands in `CustomFieldData` (`[JsonExtensionData]`) and is written by `SchemaFieldMapper` against the document schema, so a new field needs a schema change, not a model change.

| Model | Typed properties |
| --- | --- |
| Employee | `dbCode`, `ssn` (9 digits, no dashes), `name`, `firstName`, `lastName`, `deleted`, subrecord lists `i9Statuses`, `directDeposits`, `taxWaivers`, `permanentDeductions` |
| Deal Memo | `dbCode`, `employee` (SSN), `productionCompany`, `project`, `effectiveDate`, `expirationDate`, `deleted`, `guarantees` (with `guaranteeInclusions`), `allowances` |
| Corporation | `dbCode`, `taxId`, `name`, `corpType`, `deleted`, `corpRegistrations` |

The deal memo has **no** `number`: Payroll assigns it after HTGPlus persists the record.

### Field name corrections vs. the HTGPlus REST API

The contract field names differ from the REST API names. Use the contract names everywhere in this service.

| Concept | Contract field (use this) | REST API field (do NOT use) |
| --- | --- | --- |
| Workers Comp code | `wciCode` | `wcompCode` |
| Pension union | `pensionUnion` | `pensionUnionId` |
| Vacation/holiday union | `vacationHolidayUnionId` | `htgUnionId` |
| Guarantee type | `type` | `guaranteeType` |
| Hourly rate on guarantee | `rate` | `hourlyRate` |
| Non-taxable pay code on allowance | `payCode1` | `nonTaxablePayCode` |
| Taxable pay code on allowance | `payCode2` | `taxablePayCode` |
| Deal memo number | absent (not in payload) | `dealMemoNumber` |
| Employee ref in deal memo | `employee` (SSN string) | `empId` (internal GUID) |

---

## 6. Persistence

### Per-set transaction

Each set opens and commits its own transaction (`IHtgScopedTransactionRunner.RunAsync`) inside the materializer loop. A failing set costs only that set; the others in the package are unaffected ([LL#3](https://castandcrew.atlassian.net/wiki/spaces/HPS/pages/5338824707/Backend+Code+Review+Lessons+Learned)). Order inside the transaction:

1. Corporation upsert (when present).
2. Employee upsert with its subrecords.
3. Deal Memo insert with guarantees, inclusions and allowances.
4. Mark `referenceId` processed, recording the package and the IDs of the documents created (§8).
5. `ProduceAfterCommit` of the set-materialized trigger (below).

For every document the providers clear the Redis cache and queue the Elasticsearch re-export; the export message is produced only after the transaction commits.

### Entities

| Entity | Upsert key / ExternalId | Behavior |
| --- | --- | --- |
| Corporation | `taxId` (COMN key of type `corporation`) | Update if it exists, insert if not. Registrations stored as `CorporationSubRecord` documents. Create-time defaults apply to new records only. |
| Employee | `dbCode` + dashed SSN (`NNN-NN-NNNN`); `Code` is the dashed SSN | Update if it exists, insert if not. Display name defaults to "LastName, FirstName MI" when not supplied. Subrecords (`i9Statuses`, `directDeposits`, `taxWaivers`, `permanentDeductions`) are stored as linked `EmployeeSubRecord` documents. |
| Deal Memo | `dbCode\|ssn\|0`; `Code = "0"` | **Always insert**, `Status = Pending`. Guarantees and allowances are stored as linked `DealMemoGuarantee` / `DealMemoSubRecord` documents (`ParentExternalId` = the deal memo's ExternalId). |

### Schema-driven field writing

`SchemaFieldMapper` iterates the document type's registered field metadata and writes each field from the message (typed properties passed explicitly, the rest from `CustomFieldData`). Document-typed (lookup) fields are written as the target document's ExternalId, built by `LookupIdBuilder` (see gap G7). Defaults fill a field only when the message supplied nothing for it.

### Region

Every Employee, Deal Memo, Corporation and subrecord row needs a Region. It is read from the project document (`ProjectRegionLookup`, key `dbCode|productionCompany|project`); records without their own production company/project use the set's deal memo (see G5).

### Set-materialized trigger

After a set commits, the service produces to `htg-onboarding-set-materialized` a message keyed by `referenceId` with `packageId`, `referenceId`, `employeeId`, `dealMemoId` and, when present, `corporationId` (the persisted document IDs). `HTGPlus.OnboardingImportProcessor` (H2G-6949) consumes it to send the set to Payroll; this service has no dependency on it.

---

## 7. Identity

| Identity | Defined by | Used for |
| --- | --- | --- |
| Ingestion package ID (`packageId`) | IPCP (`packageTrackingId`), via ExternalDataMapper and AiCOE | Identifies the package. Recorded with each processed set so the sets and documents created from one package can be resolved (§8). The package row itself is saved upstream by ExternalDataMapper's IPCP intake. |
| `referenceId` | AiCOE, via ExternalDataMapper | Identifies the set. Idempotency key and correlation key for issues. Never generated by HTGPlus. |
| Natural key | The data | Employee: `dbCode` + `ssn`. Corporation: `taxId`. Decides insert vs. update. |
| Document ID (GUID) | This service | Primary key of the persisted Employee / DealMemo / Corporation (the existing document's ID on update, a new GUID on insert). Returned in the set-materialized trigger and recorded in the processed-reference row. |

The message carries no document IDs. This service is the only one that knows whether a record already exists, so it is the only one that can assign the ID.

---

## 8. Idempotency and package linkage

- `OnboardingParser` skips a set whose `referenceId` is already in `OnboardingProcessedReference` (logged at Information, counted as skipped; never throws).
- The materializer inserts the `referenceId` inside the same transaction as the set's writes, so a set is marked processed if and only if it was committed.
- On a retry of a partially failed message, sets already committed are skipped and only the failed ones are re-attempted.

### Set-to-package link ([H2G-7185](https://castandcrew.atlassian.net/browse/H2G-7185))

The Onboarding Dashboard (H2G-6967) shows, for each ingestion package, the Employee, Deal Memo and Corporation documents created from it. For each successfully processed set, `OnboardingProcessedReference` therefore also records the package the set came from and the IDs of the documents created (the corporation ID only when the set has a corporation), so the dashboard can resolve them. The row stays the single record of "this set was processed" and is written in the same transaction as the set.

- This needs a database migration on `OnboardingProcessedReference`; it must be applied **before** OnboardingImport is deployed.
- Status: in development. Column names and the migration script are defined in the ticket's implementation.

---

## 9. Issues storage (target)

ExternalDataMapper produces validation issues and sends them in the message's `issues[]`. This service stores them without interpreting them, in an `OnboardingIssue` table:

| Column | Source |
| --- | --- |
| `IssueKey` (unique) | Deterministic key from the message; a redelivery cannot create duplicates. |
| `PackageId`, `ReferenceId` | Message / issue. `ReferenceId` is null for package-level issues. |
| `EntityType`, `Field`, `Code`, `Severity`, `Decision`, `ValueInfo` | Issue fields as produced upstream; `ValueInfo` is type/length only, never a raw value. |
| Created date | Set when stored. |

Issues are stored independently of whether a set later materializes, so a rejected set (which never reaches the materializer) is still recorded. Errors for an entity are queried by `ReferenceId` + `EntityType` + `Field`; no document ID is needed. See G2.

---

## 10. Error Handling

`OnboardingRetryCoordinator` owns the retry decision for both handlers. It follows the PayrollRouter / ExternalDataRouter pattern: re-queue with an incremented attempt counter, log at exhaustion, no separate dead-letter topic.

| Case | Behavior |
| --- | --- |
| Set fails to persist (exception in the set's transaction) | Caught per set, logged with `referenceId`, `packageId` and masked SSN; remaining sets continue. The message is then retried as a whole (committed sets are skipped by idempotency). |
| Unhandled exception in parse or materialize | Envelope re-produced to the internal retry topic (`OutOnboardingInternal` → `InOnboardingInternal`, same physical topic) with `AttemptCount + 1`, `FirstFailedAt`, `LastError`. |
| Attempts reach 5 (`MaxAttempts`) | Error log with a `[DLQ]` marker (packageId, key, last error); no further re-queue. |
| Parse failure (invalid JSON, no sets) | Logged as an error and the message is dropped without retry (see G11). |
| Duplicate set | Skipped (§8); never an error. |

The internal retry topic must be provisioned per environment and mapped through `TOPICS_CONSUME` / `TOPICS_PRODUCE`. A failure to re-produce is logged and that message is not retried. Logs never contain a raw SSN (`***-**-last4`) and carry `referenceId` / `packageId` through a log scope ([LL#11](https://castandcrew.atlassian.net/wiki/spaces/HPS/pages/5477957635/LL+11+Logging+why+a+traceable+ID+is+not+enough)).

Warning and error logs also include the key package fields from the message root (H2G-7185), so a failure can be traced to its ingestion package without querying the database. Which fields are "key" is defined in the ticket's implementation.

---

## 11. Dependencies

| Dependency | How used |
| --- | --- |
| `HTGPlus.CommonCore`, `HTGPlus.DocumentCore` | Base domain entities, Active Record, document schemas and keys. |
| `HTGPlus.EmployeeSetupCore`, `HTGPlus.CorporationCore`, `HTGPlus.ProjectCore`, `HTGPlus.ProductionCompanyCore` | Employee, Corporation, Project and ProductionCompany document types. |
| `HTGPlus.RuleCore` | Export/reindex helpers. |
| Kafka client (existing) | Consumers, retry producer, set-materialized trigger. |
| SQL Server (existing) | Persistence via Active Record; `OnboardingProcessedReference`. |
| `HTGPlus.ExternalDataMapper` | Contract only — no project reference, no shared code. |

No new external packages required.

---

## 12. Gaps and pending decisions

Shared register — the same table is in the ExternalDataMapper design (§13). "Owner" is the service whose code changes. Statuses: **Agreed** (decided, not yet implemented), **Proposed** (recommended, awaiting decision), **Open** (needs a decision or input), **To verify**.

| ID | Gap | Current behavior | Target | Owner | Status |
| --- | --- | --- | --- | --- | --- |
| G1 | Mapper-generated employee ID | The mapper generates a new GUID per employee per run (`employee.id`). It differs on every retry, cannot apply to an existing employee, and OnboardingImport ignores it. | Mapper stops generating it. OnboardingImport assigns all document IDs (§7). H2G-6776's instruction to generate the GUID is superseded. | ExternalDataMapper | Agreed |
| G2 | Errors are log-only | Field warnings, blanked lookups and skipped sets are only written to Serilog; there is no queryable record, and a blanked lookup silently becomes null downstream. | `issues[]` in the same output message; OnboardingImport stores them verbatim in an `OnboardingIssue` table keyed by `referenceId` (§9). Needs an issue model and a collector in the mapper and walker, the table, and OnboardingImport's parser accepting an empty `onboardingSets`. | Both | Agreed |
| G3 | Data errors raised as exceptions | `Map()` throws for structural data errors (no `dbCode`, no employees, bad SSN, duplicate form). The router retries them 5 times although the outcome is deterministic. | These become issues; exceptions only for infrastructure failures. | ExternalDataMapper | Proposed |
| G4 | OnboardingImport re-validates | Its parser rejects sets missing `referenceId`, `employee.ssn` or a corporation `taxId`, all of which the mapper already guarantees. | Remove the checks; OnboardingImport persists what it receives. | OnboardingImport | Proposed |
| G5 | Region resolved twice, differently | The mapper resolves region for verification with a `dbCode` fallback. OnboardingImport resolves it separately, several times per set, and throws a `NullReferenceException` when the project is missing. | The mapper includes the resolved region in the contract; OnboardingImport reads it. | Both | Proposed |
| G6 | Contract fields nobody reads | The mapper emits `context`, `employee.federalW4` and `employee.stateW4`. OnboardingImport has no model or provider for them, so W-4 data is mapped and validated and then not persisted. | Either OnboardingImport persists W-4 (typed subrecord lists, like `i9Statuses`) or the mapper stops emitting it. | Both | Open |
| G7 | ExternalId built twice | The mapper's `DocumentFieldWalker` builds each lookup's ExternalId to verify it; OnboardingImport's `LookupIdBuilder` builds it again to persist it. The verified value and the persisted value can diverge. | The mapper emits the resolved ExternalId (the one it verified); OnboardingImport writes it as is. Changes the lookup fields of the contract. | Both | Proposed (phase 2) |
| G8 | Where defaults live | All create-time defaults are in OnboardingImport, including ones derived from project and occupation reference data. | Defaults that depend on persisted state (is the record new, the employee just stored) stay in OnboardingImport; defaults derivable from the message plus reference data could move to the mapper. | Both | Open |
| G9 | Employee defaults on update | The employee provider applies create-time defaults on every upsert; the corporation provider applies them only for a new record. For an existing employee a default can overwrite the stored value. | Apply employee create-time defaults only on insert. | OnboardingImport | To verify |
| G10 | Redundant "no schema entry" warnings | OnboardingImport warns about fields without a schema entry; the mapper is already schema-driven, so today it only fires for the fields in G1 and G6. | Remove after G1 and G6. | OnboardingImport | Proposed |
| G11 | Silent parse failures | OnboardingImport logs a permanent parse failure and drops the message without retry or DLQ entry. | Once the mapper is the only producer, a parse failure is a contract bug and must surface (error log with DLQ marker). | OnboardingImport | Proposed |
| G12 | Placeholder deal memo project | The mapper sets `dealMemo.project = 1`; AiCOE's `projectNumber` is a business reference ("PRJ-20261"), not an HTG project ID. Region resolution and the project lookup then run against the placeholder. | A real source for the HTG project. Candidate: the package metadata's `project`, which IPCP sends as an HTG project reference, the IPCP intake normalizes and AiCOE echoes back; to be confirmed against real packages. Otherwise a mapping from `projectNumber`, or an input from AiCOE. | ExternalDataMapper | Open |
| G13 | Dev test-publish endpoint | H2G-6839 describes `POST /dev/onboarding/publish`; there is no `Dev/` folder in the service on `development`. | Confirm whether it is still wanted. | ExternalDataMapper | To verify |
| G14 | Required deal memo effective date enforced by exception | The deal memo provider parses `effectiveDate` with `DateTime.Parse`, which throws when it is missing or invalid. The set is counted as failed and the whole message is retried 5 times, although the outcome is deterministic. | The mapper validates the effective date (an issue and a rejected set when absent); OnboardingImport does not enforce it. | Both | Proposed |
| G15 | ExternalDataMapper writes the ingestion package row | The IPCP intake module (H2G-7147) saves the `IngestionPackages` row, which makes ExternalDataMapper a persisting service for package-level data, while the agreed role for onboarding documents is "map and validate, never save". | Decide whether the intake module stays in ExternalDataMapper as a documented exception (the package row is not an onboarding document), or moves to a service whose role is persistence. | Both | Open |

---

## 13. Clean Code Guidelines

This service must not replicate the patterns that have made existing services hard to maintain.

- **Single responsibility:** each class does one thing. The coordinator decides retry, the parser parses, the materializer orchestrates per-set persistence, each provider persists one entity.
- **Small classes and methods:** classes around 150 lines, methods around 20; more than 4 constructor dependencies is a signal to split.
- **No "god" processors:** deserialization, business rules, persistence, logging and retry each live in their own class.
- **Explicit dependencies:** constructor injection; no service locator and no `new` inside business-logic classes.
- **Naming:** names describe intent; a reader should understand a class without reading its body.

---

## 14. Out of Scope

- Mapping, translation and validation of source data — owned by `HTGPlus.ExternalDataMapper`
- Receiving IPCP packages, saving the package row and forwarding to AiCOE — `HTGPlus.ExternalDataMapper` (H2G-7147)
- CQRS / HTGPlus.CQRS — deferred, planned for a future refactor pass
- HTTP endpoints — pure Kafka consumer
- Sending sets to Payroll and handling the deal memo number assigned back — `HTGPlus.OnboardingImportProcessor` (H2G-6949)
- The Onboarding Dashboard itself (H2G-6967) — this service only records the data it reads

---

## 15. Open Questions

1. **Kafka topic names** — the physical names behind `InOnboardingSaved` (must match ExternalDataMapper's `OutOnboardingSaved`) and the internal retry topic are set per environment through `TOPICS_CONSUME` / `TOPICS_PRODUCE`; the internal retry topic needs manual provisioning.
