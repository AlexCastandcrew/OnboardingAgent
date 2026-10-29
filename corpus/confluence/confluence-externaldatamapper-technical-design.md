# Onboarding Mapping Service (HTGPlus.ExternalDataMapper) — Technical Design

Source: Confluence page 5558992937, space HPS, last modified Oct 21, 2026.

**Ticket:** H2G-6838 (Design) → H2G-6839 / H2G-6840 / H2G-6841 (Implementation), derived from H2G-6776; IPCP package intake: H2G-7147; package metadata: H2G-7184
**Status:** Draft. Roles, identity and issue model in §2, §6 and §7 are agreed; items in §13 are pending decisions.

### Related documents

- Onboarding Import Service - Technical Design — the service that persists what this service produces (H2G-6493). Its §2 carries the same responsibility table and its §12 the same gap register.
- Onboarding Import — Remaining Work & Dependencies

---

## 1. Overview

This service has two jobs in the onboarding flow:

1. **IPCP package intake** (H2G-7147): it receives the ingestion packages IPCP publishes, saves the package row, makes it available in Elasticsearch, forwards document packages to AiCOE and replies to IPCP with the result.
2. **Onboarding mapping**: AiCOE extracts the onboarding form data (Start/Close, I-9, W-4, State W-4, W-9) from the documents of each package and publishes it back in AiCOE's own shape ("shape A"), echoing the package metadata. `HTGPlus.ExternalDataMapper` consumes shape A and produces the onboarding contract ("shape B"). It owns everything about *understanding* the data: translating it, coercing it to HTG's schema, validating it, and deciding what is rejected.

`HTGPlus.OnboardingImport` consumes shape B and only *persists* it; it does not validate or interpret the data.

The service is named *External* Data Mapper, not *Onboarding* Data Mapper: onboarding is the first source it maps, not the only one, so the internal structure is source-agnostic.

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
| Receiving IPCP packages, saving the package row, forwarding to AiCOE, replying to IPCP | Yes (IPCP intake module, §3c) | No |
| Forwarding ingestion package metadata | Yes — copies it verbatim to the message root | Records and logs it, does not interpret it |
| Document ID assignment | No | Yes |
| Insert-vs-update decision | No (onboarding documents) | Yes |
| Create-time defaults that depend on persisted state | No | Yes |
| Persistence and transactions | No for onboarding documents. The only write is the ingestion package row saved by the IPCP intake module (§3c, gap G15). | Yes |
| Idempotency on `referenceId` | Carries it through unchanged | Enforces it |
| Retry/DLQ | Own topic for the mapping flow, infrastructure failures only; IPCP intake never throws and reports failures in its reply | Own topic, infrastructure failures only |

---

## 3. Architecture

The diagrams below are pending a refresh to match §2, §3c and §6; the text in this page is the source of truth until then.

[diagram omitted]

### 3a. End-to-end flow — IPCP → ExternalDataMapper → AiCOE → ExternalDataMapper → OnboardingImport

1. IPCP publishes an ingestion package header to `InIpcpPackage`. The IPCP intake module saves it, requests the Elasticsearch export, forwards `DOCUMENTS` packages to AiCOE (`OutAicoePackage`) and replies to IPCP (`OutIpcpPackageReply`) (§3c).
2. AiCOE extracts the form data and publishes shape A onto `InExternalDataPackage`, echoing the package metadata.
3. The onboarding mapping module maps and validates shape A and publishes shape B onto `OutOnboardingSaved`, which resolves to the physical topic `HTGPlus.OnboardingImport` consumes as `InOnboardingSaved`.
4. OnboardingImport persists the sets and records, per set, the package it came from.

The two services share only the contract (§5); there is no project reference between them.

### 3b. HTGPlus.ExternalDataMapper — internal architecture

One shared, source-agnostic skeleton plus one handler/mapping module per source. Adding a source means adding a handler and a mapping type; it must not require touching another source's code.

| Component | Responsibility |
| --- | --- |
| `Program.cs` / `Configuration.cs` | Web host, Serilog → Elasticsearch, `/health`, DI, consumer and producer registration, schema registration for the types the mapper reads. |
| `ExternalDataRouter` | Consumer on `InExternalDataPackage`. Routes each `ExternalDataEnvelope` to the `IExternalDataHandler` whose `MappingType` matches (`KnownMappingTypes`). On failure re-produces to the same topic with `AttemptCount + 1` (max 5), then emits a `[DLQ]` log and stops. |
| `ExternalDataEnvelope` | `MappingType`, raw source JSON in `Data` (a JSON **string**, not a nested object: the consumer uses System.Text.Json and rejects an object with `Cannot get the value of a token type 'StartObject' as a string`), `OriginalKey`, `AttemptCount`, `FirstFailedAt`, `LastError`. |
| `ExternalDataMapperTopics` + `ITopicResolver` | Aliases `InExternalDataPackage`, `OutOnboardingSaved`, `InIpcpPackage`, `OutAicoePackage`, `OutIpcpPackageReply`, `OutIngestionPackageUpdated`; physical names come from `TOPICS_*` environment variables. A consume alias and a produce alias must never share a name. |
| `OnboardingPackageHandler` (`MappingType = "onboarding"`) | Thin entry point: map → open a log scope with the package identity (`PackageId`, `PackageName`, `ProjectId`, `UploadedBy`) → verify lookups (inside `IHtgScopedTransactionRunner`, for the ambient SQL connection) → publish to `OutOnboardingSaved` outside the scope. Does no retry itself. |
| `Models/AiCoeOnboardingPackage` | Intake model (shape A). |
| `Mapping/OnboardingPackageMapper` | Translates shape A → shape B per H2G-6738 and copies the ingestion package metadata to the message root (§5). Schema-driven: iterates the registered `employee` / `dealMemo` document schemas and reads each field's source from `EmployeeAicoeSourceMap` / `DealMemoAicoeSourceMap`; per-type translation and coercion in `*FieldTranslations` / `*FieldCoercion`; `StateCodeNormalizer` for state values. |
| `Onboarding/Verification/*` | `OnboardingDocumentVerifier` resolves the region once per set and runs one `IOnboardingEntityVerifier` per entity (employee, dealMemo, corporation). `DocumentFieldWalker` walks the schema, finds document-typed (lookup) fields and checks each resolves via `IDocumentProbe`. |
| `IpcpIntake/*` | IPCP package intake module, described in §3c. |
| `TimecardPackageHandler` | Scaffold for a further source (`MappingType = "timecard"`). |

The service is not purely stateless: lookup verification reads HTG reference data (SQL/Redis) through the probe, and the IPCP intake module writes the ingestion package row. Verification reads reference data because validating document-typed fields requires it, which is why the handler wraps it in the scoped transaction runner.

### 3c. IPCP package intake (H2G-7147)

Onboarding packages from IPCP used to be consumed by the Payroll base importer (`ImportPackage` in `HTGPlus.ImportBase`), which is the wrong domain and runs on the old Kafka consumer. Intake now lives in this service, on the new Kafka consumer, without adding pods; `ImportPackage` is retired from ImportBase in the same release.

| Component | Responsibility |
| --- | --- |
| `IpcpPackageHandler` | Kafka entry point (`InIpcpPackage`, topic `ipcp-htg-packages`). Hands the raw message to the intake service and replies to IPCP on `OutIpcpPackageReply` with `packageTrackingId`, `htgPackageTrackingId` and `message`. Never throws: a failed reply is logged so it cannot stall the consumer loop. |
| `IpcpPackageIntakeService` | Validates the package, normalizes the project, saves the row, requests the Elasticsearch export and forwards the package to AiCOE. Never throws: every failure becomes a note in the outcome, which IPCP receives in the reply. |
| `IpcpPackageHeader` | The package header as IPCP publishes it; unknown fields are kept in `Extra` so they travel to AiCOE with the rest. |
| `IIngestionPackageStore`, `IPackageProjectLookup` | Save through the existing `IngestionPackages_upsert` stored procedure; look up the project by its external id (its payroll coordinator is stored with the row). |

Processing of one package, in order:

1. The package must carry a `project` and a `packageTrackingId` that is a valid GUID; otherwise IPCP is told and nothing is saved.
2. The project (`dbCode|…`) is normalized: the year in the `dbCode` is stripped (`ToPlusId`), which is the form HTG looks the project up by; a project without `|` is not valid. The normalized value is what is stored and forwarded.
3. Inside the scoped transaction the row is upserted (idempotent). An unknown project is reported in the reply.
4. After the transaction commits: request the Elasticsearch export on `OutIngestionPackageUpdated`. A failure here is logged and does not block the next steps; the DataChanges poller re-publishes the row.
5. Packages with `packageType = DOCUMENTS` are forwarded to AiCOE on `OutAicoePackage` (physical topic `{env}-htg-aicoe-document-package`): the full header plus `packageId`, `packageTrackingNumber` (HTG's numeric id) and `projectId`. Other package types are saved but not forwarded. Filtering which packages go to AiCOE through a rule table is out of scope for H2G-7147.
6. IPCP receives the reply: empty `message` on success, otherwise the notes of what failed. The reply carries no HTG tracking id when the row was not saved.

Produces are awaited and run only after the commit, never inside the transaction block, so a failure can be reported in the reply (`ProduceAfterCommit` is fire-and-forget).

Deployment: the new consumer uses its own consumer group (`…externaldatamapper.processor.InIpcpPackage`, offset reset `Earliest`). The committed offsets of the old importer group for `ipcp-htg-packages` must be copied to it before the first deploy, otherwise the retained history is replayed (duplicate AiCOE forwards and IPCP replies). The topic `{env}-htg-aicoe-document-package` must exist, and `IngestionPackages_upsert` must exist in each environment. During a rolling deploy old and new pods briefly overlap; the upsert is idempotent, the reply and the AiCOE forward are not.

Status: merged (H2G-7147, PR #19321, 2026-10-15); present in `development` and `majorfeature_2.0`. AiCOE's side of the contract (topic name, field list) is tracked in AICOE-368.

### Schema sources

`HTGPlus.DocumentCore` is read, never modified. The mapper reads the registered employee and dealMemo schemas (by document type string) to decide which fields exist, and keeps the AiCOE source of each field in its own `EmployeeAicoeSourceMap` and `DealMemoAicoeSourceMap`, with translation and coercion in `*FieldTranslations` and `*FieldCoercion`. The registered document schemas remain the source of truth for the output shape.

---

## 4. Scope of this service

| Piece | File(s) |
| --- | --- |
| IPCP package intake | `IpcpIntake/*` |
| Intake model (shape A) | `Models/AiCoeOnboardingPackage.cs` |
| Field mapping (A → B) and package metadata forwarding | `Mapping/*` |
| Lookup verification | `Onboarding/Verification/*`, `Documents/*` |
| Kafka entry points and retry | `Handlers/*` |
| Topic aliases and publish | `ExternalDataMapperTopics.cs`, `Configuration.cs` |

Apart from the ingestion package row (§3c), nothing in this service writes domain documents. Changes to the contract it produces (§5) are coordinated with `HTGPlus.OnboardingImport`; the pending ones are listed in §13.

---

## 5. JSON Contract

### Input (shape A — from AiCOE)

```json
{
  "dbCode": "1177",
  "productionCompany": "88421",
  "projectNumber": "PRJ-20261",
  "extractedAt": "2026-09-02T14:30:00Z",
  "metadata": { "packageId": "...", "packageName": "...", "uploadedBy": "..." },
  "employees": [
    {
      "employeeIdentity": { "referenceId": "ONB-2026-000482", "employeeId": "EE-108234" },
      "forms": [
        { "formType": "Start/Close", "fields": [ { "field": "Social Security Number, SSN", "value": "<ssn>" } ] },
        { "formType": "I-9", "fields": [ "..." ] },
        { "formType": "W-4", "fields": [ "..." ] },
        { "formType": "State W-4", "state": "CA", "fields": [ "..." ] },
        { "formType": "W-9", "fields": [ "..." ] }
      ]
    }
  ]
}
```

| Field | Required | Notes |
| --- | --- | --- |
| `dbCode` | Yes | Payroll company code. |
| `employees[]` | Yes | At least one entry. |
| `employees[].employeeIdentity.referenceId` | Yes | Idempotency key, carried through unchanged. |
| `employees[].forms[]` | Yes | `formType` + flat `{field, value}` list. `State W-4` may repeat (one per state). `Start/Close`, `I-9`, `W-4`, `W-9` must appear at most once per employee. |
| `productionCompany`, `projectNumber`, `extractedAt` | No | Package-level context. |
| Package metadata (`packageId`, `packageName`, …) | No | The ingestion package metadata AiCOE echoes back from the package HTG forwarded to it (§3c). Read from the message root or from a nested `metadata` object; see "Package metadata" below. |

An employee entry with neither a Start/Close nor an I-9 form is a dues/benefit-only submission, not a new hire; it is skipped (logged at Information), not treated as an error.

### Output (shape B — target contract)

```json
{
  "packageId": "ingestion-package-id",
  "packageName": "...",
  "project": "...",
  "uploadedBy": "...",
  "onboardingSets": [
    {
      "referenceId": "ONB-2026-000482",
      "corporation": { "taxId": "<ein>", "...": "..." },
      "employee": { "ssn": "<ssn>", "firstName": "...", "i9Statuses": [ "..." ] },
      "dealMemo": { "employee": "<ssn>", "guarantees": [ "..." ], "allowances": [ "..." ] }
    }
  ],
  "issues": [
    {
      "issueKey": "<deterministic>",
      "referenceId": "ONB-2026-000482",
      "entityType": "employee",
      "field": "dateOfBirth",
      "code": "INVALID_DATE",
      "severity": "warning",
      "decision": "omitted",
      "valueInfo": "string(length=10)"
    }
  ]
}
```

- The root carries `packageId` and, when AiCOE sent them, the package metadata fields (see below). Messages without metadata keep the previous shape.
- `onboardingSets` holds only the sets that passed validation. `corporation` is present only when the employee has a valid W-9.
- The employee carries no `id`: document IDs are assigned by OnboardingImport (§6).
- `issues[]` (target, G2) is at package level and covers every set, including rejected ones, so a rejected set is still recorded. A package where every set is rejected is published with an empty `onboardingSets` and its issues.
- Issue fields: `issueKey` is deterministic (derived from packageId, referenceId, entity type, field and code) so a redelivered message cannot create duplicates; `referenceId` is null for package-level issues; `entityType` is one of `package`, `employee`, `dealMemo`, `corporation`, or a subrecord type; `decision` is `omitted`, `defaulted`, `blanked` or `rejected`; `valueInfo` is type/length only, never the raw value. The exact code list is defined when this is implemented.
- Shape B still carries fields OnboardingImport does not read today (`context`, `federalW4`, `stateW4`); see gap G6.

### Package metadata (H2G-7184)

The Onboarding Dashboard (H2G-6967) shows, for each ingestion package, the Employee, Deal Memo and Corporation documents created from it. For that, the package must be identifiable downstream, so ExternalDataMapper forwards the ingestion package metadata at the root of the onboarding message. It is pass-through: the mapper does not validate or interpret it. The metadata is the header the IPCP intake module forwarded to AiCOE (§3c), which AiCOE echoes back.

| Rule | Behavior |
| --- | --- |
| Fields forwarded | `packageName`, `projectId`, `source`, `packageType`, `packageLocation`, `createdDate`, `userTimeZone`, `numberOfDocuments`, `numberOfTimecards`, `documentPageNumbers`, `ipcpReceivedDate`, `ipcpProcessedDate`, `ipcpFileStatus`, `humanIntervention`, `uploadedBy`, `refernacePackageId`, `bucketName`, `fileKey`. These are the property names of `IngestionPackage`, on the way in and on the way out (H2G-7269): no field travels under another name. The IPCP header uses `project` and `systemLocation`, and `payrollPriority` is not forwarded. `refernacePackageId` is misspelled in the entity itself (also the name of the `IngestionPackages` column) and is kept as is; a package that sends `referencePackageId` has it ignored. |
| Where it is read from | The root of the package JSON, next to `employees`. Only the root is read: a nested object is not metadata (H2G-7269). |
| Values | Copied as the exact JSON tokens received. Dates are not parsed, so they come out unchanged. |
| Optional | Every field is optional; null and absent fields are left out of the output. A message without metadata produces the previous shape. |
| Package ID | The first non-blank of: the package's own `packageId`, the Kafka message key. If none exists the package is rejected. |
| Logging | After mapping, the handler opens a log scope with `PackageId`, `PackageName`, `ProjectId` and `UploadedBy`, so every later log of the run (verification warnings, the produce, the summary) can be traced to its ingestion package. |

Status: merged (H2G-7184, H2G-7269); present in `development` and `majorfeature_2.0`. OnboardingImport's side is H2G-7185 (see its design, §5, §8 and §10).

---

## 6. Identity

| Identity | Defined by | Used for |
| --- | --- | --- |
| Ingestion package ID (`packageTrackingId` from IPCP, `packageId` downstream) | IPCP; echoed back by AiCOE | Identifies the package. A GUID, validated at intake. Forwarded to AiCOE as `packageId`, carried at the root of shape B, and recorded by OnboardingImport with each processed set to group the documents created from one package. |
| HTG package tracking ID (`htgPackageTrackingId`) | HTG, by the `IngestionPackages` upsert | Numeric identity of the saved package row. Returned to IPCP and forwarded to AiCOE as `packageTrackingNumber`. |
| `referenceId` | AiCOE (`employeeIdentity.referenceId`) | Identifies the set. Idempotency key and the correlation key for issues. Carried through unchanged; never regenerated. |
| Natural key | The data | Employee: `dbCode` + `ssn`. Corporation: `taxId`. Decides insert vs. update in OnboardingImport. |
| Document ID (GUID) | OnboardingImport | Primary key of the persisted Employee / DealMemo / Corporation. Only the persisting service knows whether the record already exists, so only it can assign the ID. |

ExternalDataMapper does not generate document IDs. An issue is addressed by `referenceId` + `entityType` + `field`, so errors can be looked up per entity without a document ID.

---

## 7. Validation and issues

Every validation outcome of the onboarding mapping flow is recorded as an issue in the output message (§5), in addition to being logged. Three levels:

| Level | Examples | Outcome |
| --- | --- | --- |
| Field | Bad date, bad number, unsupported code, unrecognized gender/ethnicity, filing status | Field omitted (or defaulted to the documented fallback), issue recorded, the rest of the set continues. |
| Lookup | A document-typed field (state, union, occupation code, pay code, …) whose value does not resolve in HTG | Field blanked, issue recorded. The `project` lookup is verified and reported but left in place. Types not verified: `corporation`, `employee` (created by the same set), `productionCompany`, `agent`, `database`. |
| Set / package | Missing `referenceId`, SSN that is not exactly 9 digits, more than one `Start/Close`/`I-9`/`W-4`/`W-9` form; package without `dbCode` or without employees | The set (or package) is rejected, an issue with `decision = rejected` is recorded. An invalid W-9 omits only the corporation. |

Lookup verification needs a region. It is resolved from the deal memo's production company and project, falling back to the region of the `dbCode`. When no region can be resolved the verification for that field is skipped and logged.

---

## 8. Entity Mapping

Full field-by-field rules: H2G-6738. Summary of behavior:

| Group | Behavior |
| --- | --- |
| Identity / address | Passthrough from Start/Close, with I-9 as fallback for name, middle initial, date of birth and permanent address. |
| SSN | Normalized to 9 digits, no dashes; the set is rejected when it is not exactly 9 digits. Found in Start/Close, falling back to I-9. |
| Gender / Ethnicity | Fixed-value translation to a small HTG code set; unrecognized or missing → `N`, with an issue. |
| I-9 status | Citizen → `N`; Non-Citizen National / Lawful Permanent Resident → `A`; Non-Citizen → `EA`; invalid form or missing → `I`. |
| W-4 (federal) | Filing status S/J/H; multiple jobs, claim amount, other income/deductions/withholding. Numeric fields keep `0`. |
| W-4 (state) | Per-state fields gated on the form's `state` (CT, MA, NY, AZ, MO, AR, WV, IN, VA, NJ, IA); `Exempt` → `exemptions = "999"`. |
| Corporation (W-9) | Built from the W-9 when valid. `taxId` = EIN (9 digits); name from "2 - Business Name", falling back to "1 - Name of Entity"; corporation type normalized to one letter; fixed defaults for country, status and date fields; address fields passed through. No valid EIN → corporation omitted with an issue. |
| Deal memo | Schema-driven fields from Start/Close; Studio/Distant guarantees and the four allowance types, each emitted only when it has a mapped amount. `project` is currently a fixed placeholder (gap G12). |

Employee-side fields are simple fixed-value translation and do not use the RuleCore fuzzy-mapping engine that Deal Memo free-text fields will need (H2G-6777).

---

## 9. Idempotency

`referenceId` is carried through unchanged into shape B. Because the mapper generates no identifiers, mapping the same package twice produces the same output, so a retry or redelivery is safe. Enforcing idempotency is OnboardingImport's responsibility (its `OnboardingProcessedReference` table). The IPCP intake upsert is idempotent on the package GUID; its reply and AiCOE forward are not (§3c).

Natural keys that decide insert vs. update in OnboardingImport (§6): employee `dbCode` + `ssn`, corporation `taxId`. A repeated `referenceId` is skipped as already processed, so a test run needs a new `referenceId` each time, and a new SSN or EIN if existing records must not be updated.

To test, insert into the input topic with Lenses as `[{ "key": "…", "value": { envelope } }]`, where `Data` is the package as a JSON string (§3b).

---

## 10. Error Handling

| Case | Behavior |
| --- | --- |
| Data problem in the mapping flow (field, lookup, set, package — §7) | Recorded as an issue in the output message. Never an exception, never retried: the outcome is deterministic. |
| Infrastructure failure in the mapping flow (SQL/Redis probe, Kafka produce) | Exception → `ExternalDataRouter` re-produces to `InExternalDataPackage` with `AttemptCount + 1`, up to 5 attempts, then a `[DLQ]` log and stop. Only error types observed and confirmed transient are retried (LL#19). |
| Any failure in the IPCP intake | No exception and no retry: the failure becomes a note in the reply to IPCP, which is told the outcome on success and on failure. A failed Elasticsearch export request is only logged. A failed reply is logged. |

The onboarding handler has no try/catch around the mapper; the router owns the exception boundary (LL#9). The IPCP intake is the deliberate exception: its consumer loop must not drop a package silently, so the intake catches and reports.

Logs carry identifiers and the shape of a value (`string(length=n)`), never a raw SSN or full form payload (LL#11). Once a package is identified, the log scope carries the package identity (§5, §3c). All log messages use the `[ExternalDataMapper]` prefix and go to Serilog → Elasticsearch, the same pattern as the other processors; there is no Lenses/DLQ topic for this service.

---

## 11. Dependencies

| Dependency | How used |
| --- | --- |
| `HTGPlus.DocumentCore`, `HTGPlus.EmployeeSetupCore`, `HTGPlus.ProducerCore`, `HTGPlus.ProjectCore` | Document schemas read for mapping, and the document lookups used for verification and region resolution. |
| `HTGPlus.DataAccess` | IPCP intake: calls the `IngestionPackages_upsert` stored procedure. |
| `HTGPlus.KafkaCore`, `HTGPlus.KafkaCore.MultiConsumer` | Consumer and producer registration. |
| `HTGPlus.Common`, `HTGPlus.CommonCore`, `HTGPlus.RuleCore` | Shared utilities, configuration and helpers. |
| `HTGPlus.OnboardingImport`'s contract | Data shape only — no project reference, no shared code. |

No new external packages required.

---

## 12. Clean Code Guidelines

Same non-negotiables as H2G-6493 (single responsibility, ~150-line classes, ~20-line methods, explicit constructor injection, no "god" processors), plus:

- **Handler stays thin** (LL#5): business rules live in the mapper, verifiers and the intake service, not the handler.
- **No defensive try/catch around the mapper call** (LL#9).
- **Field-level failures never abort the whole message** (LL#14): they become issues.
- **No speculative retry** (LL#19).
- **Layers stay source-agnostic:** handlers, models and mapping are per-source and side by side; nothing onboarding-specific (form types, field names) leaks into shared code such as the router.

---

## 13. Gaps and pending decisions

Shared register — the same table is in the OnboardingImport design (§12). "Owner" is the service whose code changes. Statuses: **Agreed** (decided, not yet implemented), **Proposed** (recommended, awaiting decision), **Open** (needs a decision or input), **To verify**.

| ID | Gap | Current behavior | Target | Owner | Status |
| --- | --- | --- | --- | --- | --- |
| G1 | Mapper-generated employee ID | The mapper generated a new GUID per employee per run (`employee.id`): it differed on every retry, could not apply to an existing employee, and OnboardingImport ignores it. Removed in `majorfeature_2.0` (H2G-7218, merged 2026-10-20); `development` still generates it until the next sync. | Mapper stops generating it. OnboardingImport assigns all document IDs (§6). H2G-6776's instruction to generate the GUID is superseded. | ExternalDataMapper | Done in majorfeature_2.0 (H2G-7218); pending in development |
| G2 | Errors are log-only | Field warnings, blanked lookups and skipped sets are only written to Serilog; there is no queryable record, and a blanked lookup silently becomes null downstream. | `issues[]` in the same output message (§5); OnboardingImport stores them verbatim in an `OnboardingIssue` table keyed by `referenceId`. Needs an issue model and a collector in the mapper and walker, the table, and OnboardingImport's parser accepting an empty `onboardingSets`. | Both | Agreed |
| G3 | Data errors raised as exceptions | `Map()` throws for structural data errors (no `dbCode`, no employees, bad SSN, duplicate form). The router retries them 5 times although the outcome is deterministic. | These become issues (§7); exceptions only for infrastructure failures. | ExternalDataMapper | Proposed |
| G4 | OnboardingImport re-validates | Its parser rejects sets missing `referenceId`, `employee.ssn` or a corporation `taxId`, all of which the mapper already guarantees. | Remove the checks; OnboardingImport persists what it receives. | OnboardingImport | Proposed |
| G5 | Region resolved twice, differently | The mapper resolves region for verification with a `dbCode` fallback. OnboardingImport resolves it separately, several times per set, and throws a `NullReferenceException` when the project is missing. | The mapper includes the resolved region in the contract; OnboardingImport reads it. | Both | Proposed |
| G6 | Contract fields nobody reads | The mapper emits `context`, `employee.federalW4` and `employee.stateW4`. OnboardingImport has no model or provider for them, so W-4 data is mapped and validated and then not persisted. | Either OnboardingImport persists W-4 (typed subrecord lists, like `i9Statuses`) or the mapper stops emitting it. | Both | Open |
| G7 | ExternalId built twice | The mapper's `DocumentFieldWalker` builds each lookup's ExternalId to verify it; OnboardingImport's `LookupIdBuilder` builds it again to persist it. The verified value and the persisted value can diverge. | The mapper emits the resolved ExternalId (the one it verified); OnboardingImport writes it as is. Changes the lookup fields of the contract. | Both | Proposed (phase 2) |
| G8 | Where defaults live | All create-time defaults are in OnboardingImport, including ones derived from project and occupation reference data. | Defaults that depend on persisted state (is the record new, the employee just stored) stay in OnboardingImport; defaults derivable from the message plus reference data could move to the mapper. | Both | Open |
| G9 | Employee defaults on update | The employee provider applies create-time defaults on every upsert; the corporation provider applies them only for a new record. For an existing employee a default can overwrite the stored value. | Apply employee create-time defaults only on insert. | OnboardingImport | To verify |
| G10 | Redundant "no schema entry" warnings | OnboardingImport warns about fields without a schema entry; the mapper is already schema-driven, so today it only fires for the fields in G1 and G6. | Remove after G1 and G6. | OnboardingImport | Proposed |
| G11 | Silent parse failures | OnboardingImport logs a permanent parse failure and drops the message without retry or DLQ entry. | Once the mapper is the only producer, a parse failure is a contract bug and must surface (error log with DLQ marker). | OnboardingImport | Proposed |
| G12 | Placeholder deal memo project | The mapper sets `dealMemo.project = 1`; AiCOE's `projectNumber` is a business reference ("PRJ-20261"), not an HTG project ID. Region resolution and the project lookup then run against the placeholder. | A real source for the HTG project. Candidate: the package metadata's `projectId`, which is the IPCP `project` that the intake normalizes (§3c) and AiCOE echoes back; to be confirmed against real packages. Otherwise a mapping from `projectNumber`, or an input from AiCOE. | ExternalDataMapper | Open |
| G13 | Dev test-publish endpoint | H2G-6839 described `POST /dev/onboarding/publish`; there is no `Dev/` folder in the service on `development` or `majorfeature_2.0`, and H2G-6839 was abandoned. | Closed: test messages are inserted straight into the input topic with Lenses (§9). | ExternalDataMapper | Closed |
| G14 | Required deal memo effective date enforced by exception | The deal memo provider parses `effectiveDate` with `DateTime.Parse`, which throws when it is missing or invalid. The set is counted as failed and the whole message is retried 5 times, although the outcome is deterministic. | The mapper validates the effective date (an issue and a rejected set when absent); OnboardingImport does not enforce it. | Both | Proposed |
| G15 | ExternalDataMapper writes the ingestion package row | The IPCP intake module (H2G-7147) saves the `IngestionPackages` row, which makes ExternalDataMapper a persisting service for package-level data, while the agreed role for onboarding documents is "map and validate, never save". | Decide whether the intake module stays in ExternalDataMapper as a documented exception (the package row is not an onboarding document), or moves to a service whose role is persistence. | Both | Open |

---

## 14. Out of Scope

- Deal Memo free-text field mapping — H2G-6777
- Document-typed field resolution with mapping-table fallback and error table — H2G-6786
- Occupation Code mapping table — H2G-6778
- Filtering which IPCP packages go to AiCOE through a rule table — a later ticket; today only `DOCUMENTS` packages are forwarded
- Persistence of onboarding documents — owned by OnboardingImport

---

## 15. Open Questions

1. **Kafka topic names** — the physical names behind the aliases in §3b are set per environment through `TOPICS_*`; the output topic must be the one OnboardingImport consumes as `InOnboardingSaved`, and `{env}-htg-aicoe-document-package` must be created for each environment.
2. **AiCOE contract stability** — per Onboarding Import — Remaining Work & Dependencies, field names and structure may still change as real data arrives (AICOE-368).
3. **Split the parser out of `OnboardingPackageMapper`?** — today one class deserializes AiCOE's JSON and maps it. Split now, or when a second source makes the duplication visible?
