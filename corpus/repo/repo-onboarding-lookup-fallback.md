# Onboarding Lookup Fallback — Design

Related: [Onboarding Mapping Service design](https://castandcrew.atlassian.net/wiki/spaces/HPS/pages/5558992937) (contract,
issue model) and [Onboarding Import Service design](https://castandcrew.atlassian.net/wiki/spaces/HPS/pages/5422841860)
(stores the issues).

---

## Summary

**Why.** Onboarding data comes from AI extraction of scanned forms. A form says "NEW YORK" where HTG's code is "NY", or
"EQUIP" where HTG says "EQR". Any value that does not match an HTG record exactly is blanked, so a legitimate value is lost
and the employee or deal memo arrives incomplete, with nothing telling the user why.

**The design**
- **The project places the package.** Every package names its project once. The region, payroll database, client,
  producer and production company are read from that project, once per package.
- **A package that cannot be placed is dropped.** It is not published and not retried; one error is logged naming what is
  missing.
- **HTG's own match first, the translation tables second.** A value HTG already recognizes is never sent to a table. A value
  that does not match is looked up in the same Data Mapping tables timecard imports already use.
- **Nothing is blanked silently.** An untranslatable value is blanked and reported as an issue, which the onboarding import
  stores on the record it belongs to.
- **A row means the same thing everywhere:** a mapping row behaves the same for onboarding as for timecards.
- **Edits apply to the next package,** with no restart.

**What it gives the business.** Fewer blanked fields on AI-onboarded employees. A recurring wording difference is fixed by
adding a row in the existing Rule Tables screen instead of by a code change. Every field still blanked is visible to the
user who has to fix it.

**What we need from others**
- The AI extraction team sends back the project HTG gave it with every onboarding package.
- The onboarding import service stores the reported issues.
- Administrators populate the Data Mapping rule tables for the projects that need them.

**Risks we accept**
- A package whose project, or anything the project points at, is missing from HTG is dropped: it creates no employees and
  is not retried. The error is in the logs only. The reference data is fixed and the user uploads the package again.
- A mapping row restricted to one source is never matched: packages carry no source, so only rows for any source match.
- A mapping row an administrator points at another payroll database's record fills in a code the import cannot find. The
  user sees the field empty or wrong and fixes it; the administrator corrects the row.

**Scope.** Lookup fields (values naming another HTG record) of an onboarding employee, deal memo and corporation, for eight
types: state, city, country, pay code, union, occupation code, work schedule, location type.

---

## 1. Problem

The business already records how outside wording maps to HTG's codes ("NEW YORK" → "NY") in the Data Mapping rule tables,
and timecard imports use them. Onboarding never reads them: any lookup value that is not an exact HTG code is blanked, and
the user is not told. Nothing is malfunctioning; onboarding simply never consults the tables.

## 2. Goals

- A value in the source's wording is translated through the mappings the business already maintains before it is blanked.
- A mapping row behaves the same for onboarding and for timecards.
- Every blanked field is visible to the user, against the record it belongs to. A package that cannot be placed is dropped
  and logged.
- An edit to a mapping row applies to the next package with no restart.

## 3. Architecture

```
Kafka InExternalDataPackage (mappingType = onboarding)
   │
ExternalDataRouter ─────────── exception → retry (section 8)
   │
OnboardingPackageHandler ───── map → verify (inside IHtgScopedTransactionRunner) → write issues → produce
   │
OnboardingPackageMapper ────── header + metadata pass through; employees → onboardingSets
   │
OnboardingDocumentVerifier ─── package context once; per set: contract/union, then entity verifiers
   │        └── OnboardingPackageContextResolver   project → ids, or null (the package is dropped)
   │
DocumentFieldWalker ────────── per lookup field: keep / translate / blank
   │
ExternalCodeFallback ───────── document type → mapper; matched id → code
   │
RuleTableCodeMapper<TQuery> ×8  fill every match column; IRuleTableProvider.GetMatchRecord (RuleCore, in memory)
   │
Kafka OutOnboardingSaved  { packageId, metadata…, onboardingSets[], issues[] }
```

| Part | Single job |
|---|---|
| `OnboardingPackageHandler` | Map, verify, write `issues`, produce. No lookup logic, no try/catch. |
| `OnboardingPackageContextResolver` | Turn the package's project into the package-level ids, or log why it cannot and return null. |
| `OnboardingDocumentVerifier` | Resolve context once, then per set resolve contract/union and run the entity verifiers. |
| `DocumentFieldWalker` | Decide per field: keep, translate or blank, and record the issue for a blank. |
| `ExternalCodeFallback` | Pick the mapper for the field's type and read the matched document's code. |
| `RuleTableCodeMapper<TQuery>` | Build one complete rule-table query and return the matched id. |
| `OnboardingIssueCollector` | Hold the package's issues; one instance per package. |

The mapper runs before the project is checked: mapping is in-memory with no reads, so checking first saves nothing. A
package the verifier drops is not produced. Verification runs inside `IHtgScopedTransactionRunner` (read-only; it supplies
the ambient SQL connection); the produce to `OutOnboardingSaved`, keyed by the original Kafka key, happens after the scope
closes.

## 4. Package context

**Input.** `projectId` (the IngestionPackage name, H2G-7269) is read from the root of the package JSON, next to `employees`; `packageId` is the first non-blank of the root
`packageId` and the Kafka key (mapping service design §5). `projectId` is the project's full external id,
e.g. `PAY26|67412|1`.

**Resolution** (`OnboardingPackageContextResolver.ResolveAsync`), in order; a miss at any step drops the package:

| Step | Read |
|---|---|
| 1 | `projectId` present |
| 2 | `Document.GetByPlusIdAsync<Project>(projectId, "project")`. The plus id drops the year (`PAY26\|67412\|1` → `PAY\|67412\|1`), so the year in the value never matters. |
| 3 | `project.Region` present; region document by that external id |
| 4 | `htgDatabase` by `project.DbCode` |
| 5 | `client` by `project.ClientExternalId` |
| 6 | `producer` by `project.ProducerExternalId` |
| 7 | `productionCompany` by `project.ProductionCompanyExternalId` |

Steps 3–7 use `IDocumentProbe.GetIdByExternalIdAsync(type, externalId, region, ct) → Guid?` with the project's region. It
reads through `Document.LoadAsync`, which follows the type registry to the type's own table (`ProductionCompany` for
step 7) and reads `Documents` for an unregistered type. The probe passes the region only when
`!Document.IsCommon(type) || Document.IsRegionCommon(type)`.
The result is `OnboardingPackageContext(PackageId, DbCode = project.DbCode, Region, RegionId, DatabaseId, ClientId,
ProducerId, ProductionCompanyId, ProjectId = project.Id)`; all are required.

**Dropped package.** The resolver logs one error (`PackageId`, `Project`, the missing type and its external id) and returns
null; the verifier returns false and the handler returns without producing. Nothing is thrown: the outcome depends on
reference data, which a retry cannot change.

**Per set.** The deal memo's `htgContract` and `htgUnion` are resolved with the section 5, step 2 rule and the deal memo's
`dbCode`, contract first. A union the probe misses is looked up in the union mapping table under the set's contract, so a
union in outside wording (`IATSE 700`) still fills the union match column. A missing or unresolved value is queried as
"any" (section 6).

## 5. Field walk (`DocumentFieldWalker`)

The walker visits the `employee`, `dealMemo` and `corporation` schemas (`Document.GetCustomFieldsProvider(type).MetaData`),
recursing into embedded types and the sub-record arrays `federalW4`, `stateW4`, `allowances`, `corpRegistrations`. It skips
`HtgOnly` fields: HTG owns them and they are never in a package. For each field where `Document.IsDocument(field.Type)`:

1. **Left alone** — the value is null, blank or `0` (the mapper's "no value"), not a string or integer (logged as a
   warning), or the type is excluded:

   | Type | Why it is not walked |
   |---|---|
   | `employee`, `corporation` | Created from this same set downstream; a probe always misses. |
   | `productionCompany` | Emitted as a number OnboardingImport converts. |
   | `database` | The record's own `dbCode`; blanking it would strip the record's key. |
   | `agent` | Its schema lives in `HTGPlus.Business.Scheduler.Core`, which this service must not reference. |
   | `project` | Placed once per package (section 4). |

2. **Build the external id** exactly as OnboardingImport's `LookupIdBuilder` does:
   `Document.IsNoDbCodeType(type) ? value : Document.TransformKeyToCOMN(value, type, dbCode, false)`, where `dbCode` is the
   root record's own `dbCode`, else the package `DbCode`.
3. **Kept** — `GetIdByExternalIdAsync` finds it. No rule table is read.
4. **Translated** — `ExternalCodeFallback.TryTranslateAsync` returns a code (section 6); the field is rewritten to it.
5. **Blanked** — anything else. The field is set to `null` and one issue is recorded (section 7).

## 6. Rule-table fallback

### Which types fall back

One mapper per Data Mapping table, bound to the document type of its result column. `AddCodeMappers()` registers them;
`ExternalCodeFallback` indexes them by `SchemaType`, and that index alone decides whether a field can fall back (two mappers
for one type fail at startup). Adding a type means adding a mapper; the walker does not change. The tables are the
`HTGPlus.RuleCore` classes, never the `HTGPlus.Business.Scheduler.Core` duplicates.

| Document type | Mapper | RuleCore table | Result column |
|---|---|---|---|
| `state` | `StateCodeMapper` | `ExternalDataMappingState` | `StateId` |
| `city` | `CityCodeMapper` | `ExternalDataMappingCity` | `CityId` |
| `country` | `CountryCodeMapper` | `ExternalDataMappingCountry` | `CountryId` |
| `payCode` | `PayCodeMapper` | `ExternalDataMappingPayCode` | `payCode` |
| `union` | `UnionCodeMapper` | `ExternalDataMappingUnion` | `UnionId` |
| `occupationCode` | `OccupationCodeMapper` | `ExternalDataMappingOccupationCode` | `OccupationCodeId` |
| `workSchedule` | `WorkScheduleCodeMapper` | `ExternalDataMappingWorkSchedule` | `WorkScheduleId` |
| `locationType` | `LocationTypeCodeMapper` | `ExternalDataMappingLocationType` | `LocationId` |

### Filling the query

`RuleTableCodeMapper<TQuery>.FindMappedIdAsync` fills every match column: a column left empty never matches, so a half-filled
query looks exactly like a missing row. The RuleCore table classes already have these properties; four interfaces in
`HTGPlus.RuleCore/DataObjects/IngestionMapping/IIngestionMappingColumns.cs` declare them so the fill is written once.

| Interface (members are `Starrable<Guid>` unless noted) | Tables | Value |
|---|---|---|
| `IIngestionMappingQuery`: `region`, `Source`, `ExternalCode` (`string`) | all eight | package `RegionId`; any (packages carry no source); the raw value |
| `IIngestionMappingProjectColumns`: `ClientId`, `ProducerId`, `ProdCoId`, `ProjectId` | all except city, country | package ids |
| `IIngestionMappingContractColumn`: `ContractId` | union, pay code, occupation, work schedule, location type | set contract, else any |
| `IIngestionMappingUnionColumn`: `UnionId` | pay code, occupation, work schedule, location type | set union, else any |
| State only: `DatabaseId` | state | package `DatabaseId` |
| City only: `WorkStateId` | city | the paired state, below |

- **"Any" is `new Starrable<Guid>(Guid.Empty)`.** It matches exactly the rows holding `*`. A starred query value cannot be
  used: the engine nulls it and then throws unboxing it to `Guid`.
- **City.** A city field pairs with the state field of the same record whose name replaces `City`/`city` with
  `State`/`state` (`city` → `state`, `hireCity` → `hireState`). The city mapper resolves that state: probe first, then
  `StateCodeMapper`. If it does not resolve, the city is blanked with `CITY_STATE_UNRESOLVED` and the city table is not
  read: a lookup under "any" state could match a `*` row and fill in a city from the wrong state. A unit test fails when a
  city-typed field in the onboarding schemas has no state field under that rule.
- **Result.** `IRuleTableProvider.GetMatchRecord` (in-memory). No match, a `Guid.Empty` result, or an id whose document is
  gone or not of the field's type (warning logged) is a miss; otherwise the code from `Document.LoadByIdAsync(type, id)`
  is written to the field.
- **Refresh.** `InitializeAttribute.Initialize(eInitializeOption.All)` at startup subscribes RuleCore to the cache-reset
  message, so an edited row applies to the next package.

## 7. Issues

The handler creates one `OnboardingIssueCollector` per package and passes it to the verifier; the walker adds to it; the
handler writes it to the output's `issues` array. The shape is the one in the mapping service design (§5).
OnboardingImport stores each one as a message on the Employee, DealMemo or Corporation it names (onboarding mapping
messages design).

| Field | Value |
|---|---|
| `issueKey` | `{packageId}:{referenceId}:{entityType}:{field}:{code}`: deterministic, so a field reported twice in one run is kept once |
| `referenceId` | The set's `referenceId` |
| `entityType` | The root record: `employee`, `dealMemo`, `corporation` |
| `field` | Path inside the record, with the array index for sub-records: `residentState`, `allowances[1].payCode1` |
| `valueInfo` | `string(length=n)` |
| `value` | The value sent, at most 100 characters. A dashed SSN, a bare 9-digit run and an EIN `NN-NNNNNNN` are masked to the last 4 digits before publishing, because OCR can put one in any field and the topic is readable. |

| Code | When | Severity | Decision |
|---|---|---|---|
| `LOOKUP_UNRESOLVED` | No HTG match and no usable mapping row | warning | blanked |
| `CITY_STATE_UNRESOLVED` | City blanked because its paired state did not resolve | warning | blanked |

A translated field produces no issue. The user sees each issue on the record it belongs to; what they do next is the
Recovery column of section 8.

## 8. Failure modes

| Failure | What happens | Recovery |
|---|---|---|
| Package cannot be placed (section 4) | Dropped: error logged, nothing published, not retried. | Fix the reference data; the user re-uploads the package in IPCP. |
| Value unrecognized, no usable mapping row | Field blanked, `LOOKUP_UNRESOLVED`. | User fixes the record; add a mapping row if it recurs. |
| City whose state does not resolve | City blanked, `CITY_STATE_UNRESOLVED`. | User fixes the record; add a state row if it recurs. |
| SQL, Redis or Kafka failure | Exception propagates to `ExternalDataRouter`: re-produced up to 5 attempts, then `[DLQ] Message dropped` with the key. | Fix the cause; the user re-uploads in IPCP. |
| Package the mapper cannot read (no `dbCode`, no employees, bad SSN, duplicate form) | The mapper throws and the router retries as above, though the result never changes. Nothing reaches the dashboard. | Correct the source documents; the user re-uploads. |
| Re-producing for a retry fails | Message lost; only an `Error requeuing` log with the key. | The user re-uploads. |
| Message delivered twice (rebalance, deploy, poll timeout) | Processed again; identical output unless a row changed in between. OnboardingImport skips a set already processed by its `referenceId`, and with it that set's issues. | None. |

## 9. Scale

- Daily volume is set by IPCP uploads and is not known before go-live. Per message, the 1 MB Kafka limit at about 4 KB per
  set caps a package at about 250 sets.
- One consumer group on `InExternalDataPackage`; ordered per original key (one per package), so packages are not ordered
  against each other. More throughput: add consumers up to the partition count.
- Per set: one cached probe per non-empty lookup field; on a miss, one in-memory rule match and one cached code read. A
  package adds the project read and five reference reads. The walk never writes.

## 10. Observability

- Every entry carries `[ExternalDataMapper]` and the handler's package scope (`PackageId`, `PackageName`, `Project`,
  `UploadedBy`), plus `ReferenceId`, `Field` and `Type` for field entries.
- Information: a translated field (`Value`, `MappedCode`). Warning: a matched row whose document no longer exists or is
  not the field's type (`Value`, `MappedId`); a field of unexpected JSON type. Error: a dropped package (`Project`, missing `Type`, its external id).
- A blank is not logged; its issue is the record. Raw values are logged only for lookup values (state, pay code and the
  like); corporation values as `[masked, length=n]`; SSNs never.
- **After deploy:** upload a package with one mapped and one unmapped value; confirm the first is translated and the second
  is in `issues`. Upload one with an unknown project; confirm one error is logged and nothing reaches `OutOnboardingSaved`.

## 11. Decisions and prerequisites

Decisions
- No manual caching: ObjectCache caches every document read and RuleCore holds the tables in memory.
- No `HTGPlus.Business.Scheduler.Core` reference; the scoped runner only supplies the SQL connection.

Prerequisites
- AiCOE echoes `projectId` at the root of the package JSON on every onboarding package.
- The mapping service passes `packageId` and the package metadata through to the output root.
- OnboardingImport stores each issue on the record it names (onboarding mapping messages design).
- Reference data exists for each project in use: region, htgDatabase, client, producer, production company.

## 12. Scenarios

1. **Normal.** A payroll coordinator uploads a start packet in IPCP for project `PAY26|67412|1`. AiCOE extracts it; the
   project resolves; `residentState = CA` probes and hits. The employee and deal memo appear with no issues.
2. **Different wording.** The deal memo says `occupationCode = EQUIP`. The probe misses; the occupation table, filled with
   the package ids and the set's contract and union, returns EQR; the field becomes `EQR`. No issue.
3. **No row.** `occupationCode = GRIP 2ND` misses on both. The deal memo arrives without it; the coordinator sees
   `LOOKUP_UNRESOLVED` for `occupationCode` on the deal memo and fills it in. An administrator adds the row.
4. **City.** `state = NEW YORK` translates to `NY` and `city = NEW TORK` is translated under NY. Had the state not
   resolved, both are blanked: `LOOKUP_UNRESOLVED` on the state, `CITY_STATE_UNRESOLVED` on the city.
5. **Project created after the upload.** The package is dropped and one error is logged naming the project. Once the
   project exists, the coordinator re-uploads and the package succeeds.
6. **Same employee in two packages, out of order.** The coordinator uploads a correction soon after the first packet. The two
   packages have different keys, so either can be processed first. OnboardingImport upserts the employee on `dbCode` + SSN,
   so the package processed last wins, and inserts a deal memo for each.

## 13. Out of scope

- The deal memo `project` placeholder and other per-record context values written by the mapper.
- Turning the mapper's structural rejections (no `dbCode`, bad SSN, duplicate form) into dashboard issues.
- Showing a dropped package on the onboarding dashboard.
- Emitting the resolved region or resolved external ids in the contract for OnboardingImport to reuse.
- Lookup types with no Data Mapping table keyed by the outside value (`htgContract`, `payrollCompany`).
- Timecard packages.

No industry pattern beyond the existing router retry is introduced, so there is no References section.
