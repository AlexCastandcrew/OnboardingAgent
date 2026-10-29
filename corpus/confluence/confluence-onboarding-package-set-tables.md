# Onboarding Import — Package Set and Document Tables: Design and Ticket Inventory

Source: Confluence page 5683740738, space HPS, last modified Oct 27, 2026.

**Status:** Scope agreed by the team (date missing in the tool output): create the set and document tables. Forms are deferred (section 10). The existing Jira tickets are updated and the two new tickets of section 7.1 are filed.
**Date:** 2026-10-26 (per page summary). Updated 2026-10-27. (The date values in the body were stripped by the tool; the dates here are taken from the page summary.)

### Related documents

- Onboarding Import — Package Linkage, Counts and Retry Design (package linkage, counts and retry; this page extends it)
- Onboarding Mapping Service (HTGPlus.ExternalDataMapper) — Technical Design (its section 2 holds the mapper and import responsibility boundary)
- Onboarding Import Service - Technical Design
- Onboarding Import and External Data Mapper — Ticket Inventory
- Onboarding Import - File Tracking and AI Data Visibility

---

## 1. Summary

**Need.** Onboarding must record which documents (Employee, Deal Memo, Corporation) each onboarding package produced. With that record, users can ask for one employee which packages the employee came in, and the system can tell whether every document of a package has been sent to Payroll and has its Payroll key, so the package can be marked complete.

**Proposal.** Parent-child tables, flat rows, all written by OnboardingImport:

- **Level 1:** `IngestionPackages`, unchanged.
- `OnboardingPackageSet`**:** one row per set per package. A set is the unit the onboarding message already uses: one person's Employee and Deal Memo and, when they exist, the Corporation. It holds the idempotency key of the set and is the parent of the document table.
- `OnboardingPackageDocument`**:** one row per document created from a set, with `DocumentType` and `DocumentId`.

The existing `OnboardingProcessedReference` table is replaced by these tables. OnboardingImport stops using it in the first change; the table is dropped later, once no deployed version uses it (section 5). It is not used in production.

ExternalDataMapper keeps its agreed role: it understands the AiCOE data and sends it in the message. It writes nothing for this design. OnboardingImport writes the rows in the same per-set transaction that saves the documents.

**Not proposed:** using the Employee and Corporation subrecords. They link to the parent by SSN and carry no package column, and every Employee read loads all of them.

**Not in this design:** storing the forms of the package. See section 10.

**What we ask reviewers to decide:** the tables and their names (sections 4 and 5), the open questions of section 6 and the ticket changes of section 7.

---

## 2. Current state

| Fact | Where it comes from |
| --- | --- |
| Only the data of Start/Close, I-9, W-4, State W-4, W-9 and Direct Deposit reaches HTG, as Employee, Deal Memo and Corporation fields and subrecords. | `OnboardingPackageMapper` and the AiCOE source maps |
| The mapper skips an employee entry that has neither a Start/Close nor an I-9 form (logged at Information) and an entry whose mapping fails (logged as a warning). Those people never reach OnboardingImport, so they have no set row and no document row. | `OnboardingPackageMapper.Map` |
| In the QA2 sample package (26 employees), 16 have neither Start/Close nor I-9 and are skipped. 14 of the 26 have no complete SSN in any form. 4 people appear twice in the package under different reference ids. | Sample file `INPUT-qa2-aicoe-htg-external-data-mapper.json`; one package, so real packages may differ |
| `OnboardingProcessedReference` holds `ReferenceId` (primary key), `PackageId` and `ProcessedDate`. OnboardingImport and OnboardingImportClean use it only to skip a reference id already processed. Nothing reads the package id. The document id columns added by H2G-7185 were removed before that change merged. The table is not used in production. | `Migrations/OnboardingProcessedReference.sql`, `OnboardingIdempotencyStore`, `IdempotencyStore` of OnboardingImportClean; H2G-7356 |
| Each Employee, Deal Memo and Corporation stores the id of its package in an indexed column (`PackageTrackingId`) and, for Employee and Corporation, the time of the upload that last wrote it. This change is ready to deploy. | H2G-7360 |
| Payroll assigns a primary key to each Employee and Deal Memo, and the key is stored back on the HTG record from Payroll's reply. Corporation already does this. | H2G-6958, H2G-6953; `HTGPlus.CorporationProcessor` |
| Employee subrecords link to the Employee by `Code` (the SSN) + `DbCode` + `Type`. They have no package column. Every Employee read loads all active subrecords of the employee, whatever their type. | `EmployeeSubRecord_GetByEmployee`, `Employee.GetEmployeeSubRecords` |
| The screens that show the packages of an Employee, Deal Memo or Corporation have no data source today: the Documents sub-tabs and the package and file download on the record screens. | H2G-7211, H2G-7210, H2G-7212, H2G-7363 |

---

## 3. Where the rows are written

| Option | Description | Assessment |
| --- | --- | --- |
| **A. OnboardingImport persists (proposed)** | The import writes the set and document rows in the per-set transaction that saves the documents. | Matches the agreed roles. Atomic with the rest of the set: the document ids and the idempotency row commit or roll back together. Retry and the order-of-upload rule already live here. |
| B. ExternalDataMapper persists | The mapper writes the rows while mapping. | Breaks the agreed role (the mapper writes only the package row of the IPCP intake, listed as gap G15). The mapper does not know the document ids or whether the set was saved. A mapper retry re-produces every message and would duplicate rows unless the mapper gets its own idempotency. |
| C. Employee and Corporation subrecords | Store the link as a subrecord of the Employee or Corporation. | No package column. Adds rows loaded on every Employee read, and the design skips subrecord writes when an older upload does not overwrite the Employee, so the history would be lost. Subrecords stay for the payroll data they already hold. |

**Why A.** The mapper design places a rule by one criterion: if it can be computed from the message plus reference data it belongs in the mapper; if it depends on what is already persisted it belongs in the import. The document ids exist only after the import saves the set.

---

## 4. Proposed design

### 4.1 ExternalDataMapper

- It publishes one message per set, with the number of sets of the package and the Kafka timestamp of the package record (H2G-7355). The set content and the contract of the documents do not change.
- The message key and the skipping rules do not change.

### 4.2 OnboardingImport

- The parser does not change: a set has an Employee and a Deal Memo, and a Corporation when the person has one.
- In the transaction of one set: the documents as designed (including the order-of-upload rule), then the set row and the document rows. When an older upload does not overwrite the Employee, the document row holds the id of the Employee that already exists, and the set is still recorded.
- Idempotency is the set key (`PackageId`, `ReferenceId`). A repeated message is skipped, as in the linkage design, and so is the repeated mapper retry.

### 4.3 Tables

`OnboardingPackageSet`

| Column | Type | Notes |
| --- | --- | --- |
| `PackageId` | `NVARCHAR(200) NOT NULL` | Package id as text. Part of the key. Real packages carry a GUID; test packages do not, so the type stays text. |
| `ReferenceId` | `NVARCHAR(200) NOT NULL` | From AiCOE. Part of the key. |
| `PersonName` | `NVARCHAR(200) NULL` | The name AiCOE sent for the person. |
| `ProcessedDate` | `DATETIME2 NOT NULL` | Default `SYSUTCDATETIME()`. |

Primary key (`PackageId`, `ReferenceId`). The same reference id in another package is another row.

`OnboardingPackageDocument`

| Column | Type | Notes |
| --- | --- | --- |
| `PackageId`, `ReferenceId` | as the set | Foreign key to the set. |
| `DocumentType` | `NVARCHAR(50) NOT NULL` | The HTG document type: `employee`, `dealMemo` or `corporation`. Free text with no check constraint, so a new type needs no schema change. |
| `DocumentId` | `UNIQUEIDENTIFIER NOT NULL` | The id of the document. |

Primary key (`PackageId`, `ReferenceId`, `DocumentType`, `DocumentId`). Index on (`DocumentType`, `DocumentId`) including `PackageId` and `ReferenceId`, so the packages of any Employee, Deal Memo or Corporation are one index seek. A Corporation shared by several sets of a package has one row per set. There is no name column: the name is read from the document, and a copy would go stale.

All rows are written once and never updated.

### 4.4 How the questions are answered

| Question | Query |
| --- | --- |
| Packages of an employee | Document rows with `DocumentType = 'employee'` and the employee's `DocumentId`, then the set, then `IngestionPackages` by the package id. Select distinct packages: a package can hold the same person twice. |
| Packages of a Deal Memo or a Corporation | The same, with `DocumentType = 'dealMemo'` or `'corporation'`. |
| Documents of a package | Document rows by `PackageId`. |
| Documents of a package that have no Payroll key | Document rows of the package, joined by `DocumentType` and `DocumentId` to the Employee, Deal Memo or Corporation, keeping those whose Payroll key is empty. The column that holds the key is question 5. |
| Package totals | Count of document rows per type (distinct ids for Corporation), extending the counts of the linkage design (H2G-7359). |

The reads are Web API endpoints, not GraphQL, and follow the Swagger contract (typed response, typed example).

**Marking a package complete.** A package is complete when the query "documents of the package without a Payroll key" returns no rows and the number of set rows of the package equals the number of sets the mapper sent (H2G-7355). The second condition is needed because a set that the import rejects leaves no document row, so it would not appear in the first query. A Corporation shared by several sets is counted once.

### 4.5 Relation with the package id stored on the documents

H2G-7360 already stores on each Employee, Deal Memo and Corporation the id of its package (`PackageTrackingId`). Both stay, because they answer different questions:

- The column says which package last wrote the Employee or Corporation, or created the Deal Memo. The record screens and the search index read it with no join.
- The document table keeps the history: every package the same Employee or Corporation came in.

Neither is derived from the other. Onboarding Import writes both in the same set transaction, and the order-of-upload rule decides the column, not the table: an older upload adds its rows to the history and leaves the column as it is.

### 4.6 Where the code lives

- The entities and their read methods go in a `*Core` assembly, because the Web API and the ElasticExporter read them and cannot reference a service project.
- The new code does not reference `HTGPlus.Ingestion.Common`, which is marked for removal, nor `HTGPlus.Business.Scheduler.Core`. The package is identified by its id and joined in SQL.
- OnboardingImport moves to the set table in H2G-7356. OnboardingImportClean is not changed for now and keeps using the old table (question 3).

### 4.7 Failure and volume

- A failure writing any of the rows rolls back the whole set and follows the retry rules of the linkage design.
- Deploy order: the tables in each environment first, then OnboardingImport, then the mapper, so the import already accepts the message the mapper sends.
- Volume: one set row and up to three document rows per set. A package of 26 sets writes at most 26 set rows and 78 document rows.

---

## 5. Replacing the existing table

`OnboardingProcessedReference` is replaced, not altered. The first change (H2G-7356) creates the new tables and moves OnboardingImport to them; it does not drop the old table.

| Option | Assessment |
| --- | --- |
| Alter it | The primary key changes to (`PackageId`, `ReferenceId`), `PackageId` becomes required, and the name keeps describing a table that only marks processed reference ids. The table is not used in production, so there is nothing to protect with an in-place change. |
| **Replace it (proposed)** | Create `OnboardingPackageSet` and `OnboardingPackageDocument`. OnboardingImport uses the set table for its idempotency check. A later script drops the old table once no deployed version uses it. OnboardingImportClean still reads it, so the drop waits until that project moves to the set table or is retired. |

Names. "Set" is the word the contract and the import already use for the unit of a package (`OnboardingSet`: one corporation, one employee and one deal memo, keyed by `referenceId`). "Document" is the word of the Onboarding Documents epic for Employee, Deal Memo and Corporation. The wording "one row per employee per package" in the linkage design and in H2G-7356 becomes "one row per set per package".

No rows are copied. The table is not used in production, and the rows in QA and QA2 are test data. After the switch in those environments, a message already processed under the old table could be processed again.

---

## 6. Questions for the team and the manager

| # | Question | Who | Default if unanswered |
| --- | --- | --- | --- |
| 1 | Is the reference id stable for one person across packages, and is it unique across packages? Why does one person appear twice in one package? (H2G-7365) | AiCOE | A person is identified across packages only by the Employee it links to |
| 2 | Do the Documents sub-tabs (H2G-7210, 7211, 7212) need exactly the packages and documents this design returns? They also ask for the document status and the user who uploaded the file, which these tables do not hold. | Product / UI | List of packages with date and package id |
| 3 | When does OnboardingImportClean move to the set table? H2G-7356 changes only OnboardingImport, so OnboardingImportClean keeps using the old table and the old table is not dropped before then. | Team / manager | OnboardingImportClean stays on the old table until the team decides |
| 4 | Timeline. Fix version 18.7.0 releases on [date missing in tool output]. Silverwing Sprint 2 ends on [date missing in tool output] and already held about 77 points when the linkage tickets were added. Which tickets of section 7.3 go in 18.7.0? The two new tickets are filed with fix version 18.7.0. | Manager | H2G-7356, 7357, 7355, 7358 and 7359 in 18.7.0; the Web API ticket (H2G-7435) in the next release if the sprint is full |
| 5 | Where is the Payroll key stored on the Employee, the Deal Memo and the Corporation, and does the job that marks a package complete use the rule of section 4.4? | Team | The rule of section 4.4 |
| 6 | In which Core assembly do the new entities live? | Team | A small onboarding Core assembly |

---

## 7. Ticket inventory

Estimates are left to the team.

### 7.1 New tickets

| Ticket | Title | Service | Type | Depends on | Notes |
| --- | --- | --- | --- | --- | --- |
| H2G-7435 | Web API: packages of an Employee, Deal Memo and Corporation, and documents of a package | Core and Web API | Story | H2G-7356 | Question 6 for the assembly. Typed responses and Swagger examples. Can feed the Documents sub-tabs (H2G-7210, 7211, 7212) once question 2 is answered. No reference to Ingestion.Common or Scheduler.Core. |
| H2G-7436 | Documentation: update the package linkage design to the set and document records | Docs | Task | Design agreed | H2G-7367 is ready to deploy and is not reopened. The file `docs/onboarding/DESIGN_OnboardingPackageLinkage.md`, the linkage page and the Onboarding Import Technical Design page still describe the old table with three ids and one message per employee. |

Both are under epic H2G-7016, with fix version 18.7.0 and no assignee.

### 7.2 Existing tickets: verdicts

**Abandon: none.** H2G-7189 is already abandoned and its content lives in H2G-7359.

**Text updated in Jira**

| Ticket | Status | Conflict with this design | Change |
| --- | --- | --- | --- |
| H2G-7356 | Open | Described one row per employee with the three ids in the same row, and covered only the database. A change of the tables alone leaves the service on the old table. | Retitled and widened so one pull request delivers both: it creates the set and document tables and moves OnboardingImport to them (idempotency by package and reference id, set and document rows in the transaction of the set). It absorbs acceptance criteria 1 to 4 of H2G-7357. It does not change OnboardingImportClean and does not drop the old table. |
| H2G-7357 | Open | Held the idempotency and the document records now delivered by H2G-7356. | Reduced to requesting the update of the package in search after a set is saved. Depends on H2G-7356. |
| H2G-7355 | Open | "One message per employee", with the number of employees of the package. | "One message per set", with the number of sets. |
| H2G-7359 | Open | Counted processed employees from the old table. | Counts the document rows by type. |
| H2G-7225 | Open | Stored the issues by `referenceId` alone. | Issues keyed by package and reference id, as children of the set. |
| H2G-7185 | Ready to Deploy | Acceptance criteria 1 and 4 said the old table holds the document ids, but the ticket's own out-of-scope section excludes it and the merged change does not do it. | Those two criteria removed. No code change. |
| H2G-7358 | Open | Did not say what is recorded for an upload that does not overwrite the Employee. | New paragraph and acceptance criterion 7: the set of an older upload is still recorded, and its Employee document record holds the id of the existing Employee. |
| H2G-7361 | Open | The retry criterion checked only the Employee and the Deal Memo. | Acceptance criterion 2 also requires one set record and one document record for each, with no duplicates. |
| H2G-7365 | Open | Did not ask about the same person appearing twice in one package. | Description and acceptance criterion 4 added (question 1). |

**No change**

| Ticket | Reason |
| --- | --- |
| H2G-7364 | The tracker shows failed employees. The set is still one Employee, one Deal Memo and, when it exists, one Corporation, so the wording stays correct. |
| H2G-7210, H2G-7211, H2G-7212 | Written by product. They ask for document status and uploader, which the set and document tables do not hold. The data source is decided with question 2. |
| H2G-7360 | Ready to Deploy. It stays; section 4.5 explains how it relates to the document table. |
| H2G-7363 | Reads the package column of H2G-7360; not affected. |
| H2G-7367 | Ready to Deploy. Not reopened; H2G-7436 updates the documents it delivered. |
| H2G-7362, H2G-7366, H2G-7377 | Corporation tax id, topic retention and Employee request id: independent. |
| H2G-7176 | Raw payload in Redis for 1 day. Stays as a diagnosis aid. |
| H2G-7197, H2G-6967 | Not affected by this design. |
| H2G-7219 | Changes the same parser area as H2G-7356, so the two are sequenced. |

### 7.3 Order

1. H2G-7356 in one pull request: the tables and the OnboardingImport change. The tables are applied in each environment before the service is deployed, and the service before the mapper.
2. H2G-7355, H2G-7357, H2G-7358 and H2G-7359.
3. H2G-7435 once H2G-7356 is merged, then the screens.
4. H2G-7185, H2G-7225 and H2G-7436 are text or documentation changes and can be done at any time.

---

## 8. Risks

- **Identity.** Without a stable person key from AiCOE, "packages of an employee" works only for people that have an Employee. The people the mapper skips have no row at all.
- **Completion rule.** A package cannot be marked complete from the document rows alone: a set that is rejected leaves none. The set count of H2G-7355 closes that gap.
- **Two import implementations.** OnboardingImportClean keeps using the old table, so the old table cannot be dropped and a package processed by that project leaves no set or document rows until it moves (question 3).
- **Pull request size.** H2G-7356 holds the tables and the service change in one pull request, to deliver them together.
- **Timing.** The linkage tickets are already in the sprint (question 4).

## 9. Out of scope

- The screens themselves, the tracker retry button and the send to Payroll.
- The job that marks a package complete. This design provides the query (section 4.4).
- Changing OnboardingImportClean.
- Storing the forms of the package (section 10).

---

## 10. Deferred: forms of the package

**Decision (team chat, [date missing in tool output]).** The forms of a package are not stored now, because the UI does not need them. The set and document tables are created. A form table may be proposed later. A table with one row per form field is considered too granular and is not part of any later proposal unless a use for it appears.

**Revisit when** a screen, report or integration asks for something at form level: the Miscellaneous forms of an employee, all the forms of a package, or a form value that is not mapped to HTG today.

**What the design leaves ready.**

- Form tables would be children of `OnboardingPackageSet`, keyed by `PackageId` and `ReferenceId`. Adding them changes neither the set table nor the document table.
- The mapper would add the forms to each set of the message, and the import would write them in the per-set transaction.

**What is lost while they are not stored.**

- Miscellaneous, Work Dues and 401k forms are not read or stored today, and the forms of people the mapper skips (16 of the 26 employees of the QA2 sample, with 31 of the 122 forms) are not stored either.
- The forms of a package processed in the meantime cannot be recovered later. The raw AiCOE payload is kept 1 day in Redis (H2G-7176) and the package topic retention is 7 days (14 proposed in the linkage design), so a package cannot be replayed after that.

**Questions that come back with the forms.**

- Can AiCOE send, per form, the page range in the uploaded file? Today only the package carries page numbers, so a link to a form can only open the whole file.
- Should a person with forms only be linked to an existing Employee by an SSN found in a Work Dues or 401k form? The mapper reads the SSN from Start/Close and I-9 only.
- Retention of the form rows, and the message size with the largest real package.
- Where the field values live, if they are ever needed (question of granularity).
