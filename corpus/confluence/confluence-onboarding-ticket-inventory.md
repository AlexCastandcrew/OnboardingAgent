# Onboarding Import and External Data Mapper — Ticket Inventory

Source: Confluence page 5639012521, space HPS, last modified Oct 09, 2026.

**As of:** 2026-10-09. Jira is the source of truth for statuses; this page is a snapshot and needs a refresh when tickets move.
**Source:** project H2G, tickets under the epics H2G-6627 (Phase 1) and H2G-7016 (Phase 2), plus every ticket whose summary names ExternalDataMapper or OnboardingImport.

### Related documents

- Onboarding Mapping Service (HTGPlus.ExternalDataMapper) — Technical Design — design; its §13 is the gap register (G1–G15).
- Onboarding Import Service - Technical Design — design; its §12 carries the same gap register.

---

## 1. Overview

144 tickets were found. Each one is placed in exactly one of four groups, by the code it changes:

| Group | Tickets | Open or in progress | Delivered | Abandoned |
| --- | --- | --- | --- | --- |
| 2. HTGPlus.ExternalDataMapper | 39 | 19 | 17 | 3 |
| 3. HTGPlus.OnboardingImport | 35 | 13 | 21 | 1 |
| 4. Related tickets outside these two services | 70 | Listed for context only | — | — |

"Delivered" means Released, Deployed, Ready to Deploy or Design Accepted. The tickets filed on 2026-10-08/09 to close the gaps of the two designs are H2G-7218 to H2G-7225.

### Gap register to tickets

| Gap | Description | Ticket(s) | State |
| --- | --- | --- | --- |
| G1 | Mapper-generated employee ID | H2G-7218 | Ticket filed |
| G2 | Errors are log-only | H2G-6863, H2G-6851, H2G-7225; storage already delivered by H2G-6997 | Tickets exist; design to be aligned (see "Open points") |
| G3 | Data errors raised as exceptions | H2G-7220 | Ticket filed |
| G4 | OnboardingImport re-validates | H2G-7219 | Ticket filed |
| G5 | Region resolved twice, differently | H2G-7221 (mapper), H2G-7222 (import) | Tickets filed |
| G6 | Contract fields nobody reads (W-4) | H2G-7223 | Ticket filed |
| G7 | ExternalId built twice | None | Phase 2, no ticket yet |
| G8 | Where defaults live | None; related: H2G-7209 | Decision pending |
| G9 | Employee defaults overwrite an existing employee | None yet; related: H2G-6651 (abandoned) | Bug to be filed |
| G10 | Redundant "no schema entry" warnings | H2G-7219 | Ticket filed |
| G11 | Silent parse failures | H2G-7219 | Ticket filed |
| G12 | Placeholder deal memo project | H2G-7224; related: H2G-6777 | Ticket filed |
| G13 | Dev test-publish endpoint | None; related: H2G-6839 (abandoned) | To verify |
| G14 | Deal memo effective date enforced by exception | H2G-7220 | Ticket filed |
| G15 | ExternalDataMapper writes the ingestion package row | None; related: H2G-7147 | Decision pending |

### Open points

1. **How errors are looked up per entity.** H2G-6997 (Ready to Deploy) delivered the `dbo.OnboardingMappingMessage` table and `GET ws/onboarding/messages/{containerId}`, where `ContainerId` is the employee's correlation GUID, which was meant to become the Employee document's `Id`; the employee, deal memo and corporation error screens (H2G-6973, H2G-6977, H2G-6978) are built on it. Once the mapper stops generating that GUID (H2G-7218), the key must come from the document IDs that OnboardingImport assigns and records per set (H2G-7185). H2G-7225 as written stores issues in a new table keyed by `referenceId`; whether to reuse the existing table instead, and how to keep rejected sets (which have no document) queryable, is to be decided before either ticket is implemented.
2. **Existing tickets to realign:** H2G-6851 (mapper-written table indexed by the employee GUID), H2G-6863 (error structure), H2G-7174 (`fileKey` is already forwarded by H2G-7184), H2G-6647 (its fix is already in the materializer; verify and close) and H2G-6775 (the contract has changed since).
3. **Bug for G9** (an existing employee loses fields when onboarded again) is not filed yet.

---

## 2. HTGPlus.ExternalDataMapper

39 tickets: 19 open or in progress, 17 delivered, 3 abandoned.

(Assignee column omitted: personal data.)

### Open and in progress (19)

| Ticket | Type | Status | Summary | Notes |
| --- | --- | --- | --- | --- |
| H2G-6730 | Story | Open | Document Validation for Employee + Corporation Maintenance | Validation rules; owned by the mapper per the agreed roles. |
| H2G-6731 | Story | Open | Document Validation for Deal Memo | Validation rules; owned by the mapper per the agreed roles. |
| H2G-6738 | Story | Open | Mapping Values for Employee + Corporation Maintenance | Business rules for the mapper (field-by-field). |
| H2G-6739 | Story | Open | Mapping Values for Deal Memo | Business rules for the Deal Memo mapping. |
| H2G-6773 | Story | Open | HTG Rule Table Changes for Onboarding Ingestion |  |
| H2G-6777 | Story | Open | Onboarding Mapping Service — map Deal Memo hard-code and passthrough fields for OnboardingImport | Deal Memo hard-code/passthrough mapping; see also G12. |
| H2G-6850 | Story | In Design | [ExternalDataMapper] Fall back to Ingestion Mapping rule tables for unresolved document fields |  |
| H2G-6851 | Story | Refinement | [ExternalDataMapper] Record unresolved document fields in an onboarding mapping error table | G2. Design conflicts with the agreed roles (indexed by the employee GUID, written by the mapper); to be realigned or replaced by H2G-7225. |
| H2G-6863 | Story | Open | [ExternalDataMapper] Emit per-employee mapping errors on the OnboardingImport Kafka message | G2. Defines the issue structure that H2G-7220 and H2G-7225 depend on; to be aligned with referenceId and package-level issues. |
| H2G-7147 | Story | Ready for Code Review | Move IPCP package consumer to ExternalDataMapper with the new Kafka consumer | PR open. Adds the IPCP intake module (design §3c, gap G15). |
| H2G-7174 | Task | In Dev | LOG-TRACK: [HTGPlus.ExternalDataMapper] Propagate fileKey and fileName through OutOnboardingSaved | Overlaps H2G-7184 for fileKey; confirm what remains. |
| H2G-7175 | Task | Ready For Dev | LOG-TRACK: [HTGPlus.ExternalDataMapper] Emit file tracking events for onboarding packages |  |
| H2G-7176 | Task | Open | LOG-TRACK: [HTGPlus.ExternalDataMapper] Store AiCOE onboarding payload for AI Data screen |  |
| H2G-7184 | Story | Ready for QA | External Data Mapper: send ingestion package metadata at the root of the onboarding message | Package metadata forwarded at the message root. |
| H2G-7208 | Task | Open | ExternalDataMapper: apply rule table changes without a restart |  |
| H2G-7218 | Story | Open | External Data Mapper: stop generating the employee document ID in the onboarding message | G1. |
| H2G-7220 | Story | Open | External Data Mapper: report rejected sets and packages as issues instead of failing the message | G3, G14. |
| H2G-7221 | Story | Open | External Data Mapper: send the resolved region on each onboarding set | G5. |
| H2G-7224 | Task | Open | External Data Mapper: use the HTG project from the package instead of a fixed placeholder | G12. |

### Delivered: Released, Deployed or Ready to Deploy (17)

| Ticket | Type | Status | Summary | Notes |
| --- | --- | --- | --- | --- |
| H2G-6624 | Task | Ready to Deploy | Scaffold ExternalDataMapper: shared consumer service for external data mapping |  |
| H2G-6765 | Story | Ready to Deploy | Ingestion Mapping Rule Tables — Move to ExcelMapping Service and RuleCore |  |
| H2G-6776 | Spike | Ready to Deploy | Onboarding Mapping Service — consume AI-extracted onboarding data and produce Employee records for OnboardingImport | Design spike. Instruction to generate the employee GUID is superseded (G1). |
| H2G-6778 | Story | Ready to Deploy | Build HTG Occupation Code mapping table (RuleCore) for onboarding Deal Memo mapping |  |
| H2G-6786 | Story | Ready to Deploy | ExternalDataMapper: Resolve document-typed employee fields (with mapping-table fallback + error table) |  |
| H2G-6838 | Story | Ready to Deploy | [ExternalDataMapper] Technical design doc + implementation diagram — Onboarding Mapping Service | Original design ticket for the mapper. |
| H2G-6840 | Story | Ready to Deploy | [ExternalDataMapper] Map AiCOE intake model to OnboardingImport's OnboardingEmployee contract |  |
| H2G-6841 | Story | Ready to Deploy | [ExternalDataMapper] Publish mapped payload to OnboardingImport topic + verify Employee/DealMemo/Corporation in UI |  |
| H2G-6849 | Story | Ready to Deploy | [ExternalDataMapper] Verify document-typed employee fields against the Documents table (absorbs H2G-6848) |  |
| H2G-6885 | Task | Ready to Deploy | [ExternalDataMapper] Realign employee mapper to H2G-6776 design: EmployeeAicoeSourceMap, EmployeeFieldTranslations, schema-driven loop |  |
| H2G-6888 | Story | Ready to Deploy | [ExternalDataMapper] Replace per-source topics with a single shared topic + mappingType envelope |  |
| H2G-6889 | Story | Ready to Deploy | [ExternalDataMapper] Map W-9 form data to Corporation for OnboardingImport (per H2G-6738) |  |
| H2G-6910 | Task | Ready to Deploy | [ExternalDataMapper] Support multiple source-field synonyms per mapped attribute |  |
| H2G-7105 | Task | Ready to Deploy | ExternalDataMapper: fix Employee mapping against real AiCOE data (schema registration, per-record isolation, ethnicity, I-9 status) |  |
| H2G-7106 | Task | Ready to Deploy | ExternalDataMapper: fix DealMemo guarantees/allowances deserialization against real AiCOE shape |  |
| H2G-7107 | Task | Ready to Deploy | ExternalDataMapper: normalize Corporation.state through the shared StateCodeNormalizer |  |
| H2G-7110 | Task | Deployed | [ExternalDataMapper] Align inbound topic naming with AiCOE producer convention |  |

### Abandoned (3)

| Ticket | Type | Status | Summary | Notes |
| --- | --- | --- | --- | --- |
| H2G-6839 | Story | Abandoned | [ExternalDataMapper] Temporary ingestion endpoint + mock JSON deserialization for onboarding package | Dev endpoint never built on development (G13). |
| H2G-6848 | Story | Abandoned | [ExternalDataMapper] Complete the document + rule-table runtime (RuleCore, DB proof, schema fail-fast, deployment) |  |
| H2G-7119 | Story | Abandoned | Publish document packages to AICOE via Kafka when received from IPCP | Superseded by H2G-7147. |

---

## 3. HTGPlus.OnboardingImport

35 tickets: 13 open or in progress, 21 delivered, 1 abandoned.

(Assignee column omitted: personal data.)

### Open and in progress (13)

| Ticket | Type | Status | Summary | Notes |
| --- | --- | --- | --- | --- |
| H2G-6647 | Bug | Open | OnboardingImport: a failed set can leave partial writes committed, and isn't marked processed (risk of duplicate rows on redelivery) | Per-set transactions are already in the materializer; verify and close. |
| H2G-6775 | Task | Open | OnboardingImport: Document and share the finalized Employee/Deal Memo JSON contract with Data Extract | Contract hand-off to Data Extract; the contract changed since (see design §5). |
| H2G-7081 | Story | Open | [OnboardingImport] Add end-to-end package tracking (PackageTrackingId-style) across OnboardingImport → OnboardingImportProcessor → Payroll | End-to-end tracking across Import, Processor and Payroll. |
| H2G-7082 | Story | Open | [OnboardingImport] Define UX for partial onboarding failures (visibility, notification, retry) | Consumer side of the errors stored by H2G-7225. |
| H2G-7144 | Task | Refinement | [OnboardingImport] Adjustments to the Employee and DealMemo save hooks after review (H2G-6956 / H2G-6957) | Save-hook adjustments after review. |
| H2G-7168 | Task | Ready for QA | [OnboardingImport] Remove the HTGPlus.Business.Scheduler.Core dependency |  |
| H2G-7177 | Task | Ready For Dev | LOG-TRACK: [HTGPlus.OnboardingImport] Emit tracking events per onboarding set |  |
| H2G-7185 | Story | In Dev | Onboarding Import: link processed sets to the ingestion package and log package metadata | Set-to-package link and package metadata in logs. |
| H2G-7209 | Story | Open | Onboarding Import: stop writing the corporation Tax ID to new Employee and DealMemo records | Related to G8 (defaults). |
| H2G-7219 | Story | Open | Onboarding Import: persist only, remove re-validation and surface unreadable messages | G4, G10, G11. |
| H2G-7222 | Story | Open | Onboarding Import: use the region sent in the onboarding message | G5. |
| H2G-7223 | Story | Open | Onboarding Import: persist federal and state W-4 records | G6. |
| H2G-7225 | Story | Open | Onboarding Import: store the validation issues received in the onboarding message | G2. See open point 1: key and table to be aligned with H2G-6997. |

### Delivered: Released, Deployed or Ready to Deploy (21)

| Ticket | Type | Status | Summary | Notes |
| --- | --- | --- | --- | --- |
| H2G-6389 | Story | Ready to Deploy | Onboarding Data Extract: Define JSON Contract and Build Import Service for Employees and Deal Memos |  |
| H2G-6424 | Story | Released | Build Onboarding Import Service for Employees and Deal Memos |  |
| H2G-6493 | Architecture | Design Accepted | Design: Onboarding Import Service Architecture and Data Flow | Original design; superseded by the current design document. |
| H2G-6532 | Story | Released | Re-architect Onboarding Employee/DealMemo subrecord persistence as separate linked ORM documents |  |
| H2G-6533 | Story | Ready to Deploy | Decide + implement retry/dead-letter handling for Onboarding Import beyond error logging |  |
| H2G-6653 | Story | Ready to Deploy | OnboardingImport: make the document schema the single source of truth for field mapping (drop SetText/SetNum/SetArray) |  |
| H2G-6678 | Story | Ready to Deploy | OnboardingImport: adjust onboarding stored procedures and sync with Elasticsearch |  |
| H2G-6691 | Story | Ready to Deploy | Defaults fields for Employee Maintenance during Onboarding Documents |  |
| H2G-6692 | Story | Ready to Deploy | Defaults fields for Deal Memo Maintenance during Onboarding Documents |  |
| H2G-6697 | Story | Ready to Deploy | OnboardingImport: Ensure Employee onboarding payload renders correctly in the Employee UI |  |
| H2G-6698 | Story | Ready to Deploy | OnboardingImport: Ensure Deal Memo onboarding payload renders correctly in the Deal Memo UI |  |
| H2G-6822 | Task | Ready to Deploy | OnboardingImport: Confirm the onboarding JSON contract carries Corporation data (coordinate with AiCOE/Data Extract) |  |
| H2G-6823 | Task | Ready to Deploy | OnboardingImport: Build OnboardingCorporationProvider for Corporation persistence |  |
| H2G-6824 | Task | Ready to Deploy | OnboardingImport: Wire Corporation into the materializer flow ahead of Employee |  |
| H2G-6825 | Task | Ready to Deploy | OnboardingImport: Add unit test coverage for Corporation persistence |  |
| H2G-6874 | Task | Ready to Deploy | OnboardingImport: Verify Corporation onboarding payload renders correctly in the Corporation UI |  |
| H2G-6897 | Task | Ready to Deploy | OnboardingImport: Fix Corporation document missing fields on initial creation (Type, CustomFields, EffectiveStart/End, Status) |  |
| H2G-6921 | Story | Ready to Deploy | Defaults fields for Corporation during Onboarding Documents |  |
| H2G-6951 | Story | Ready to Deploy | [OnboardingImport] Emit trigger after each Employee/DealMemo/Corporation set is materialized |  |
| H2G-6956 | Story | Ready to Deploy | [OnboardingImport] Add ProduceAfterCommit Payroll-send hook to the Employee provider |  |
| H2G-6957 | Story | Ready to Deploy | [OnboardingImport] Add ProduceAfterCommit Payroll-send hook to the DealMemo provider |  |

### Abandoned (1)

| Ticket | Type | Status | Summary | Notes |
| --- | --- | --- | --- | --- |
| H2G-6651 | Bug | Abandoned | OnboardingImport: re-onboarding the same employee doesn't dedupe — DealMemo and subrecords always insert new rows | Re-onboarding an existing employee; related to G9. |

---

## 4. Related tickets outside these two services

Listed for context only; they are not part of the ExternalDataMapper or OnboardingImport code. Statuses as read from Jira.

### Mapping messages and error handling in the UI (consumers of the errors) (4)

- H2G-6973 — Deal Memo Screen - Error Handling for Onboarding Documents *(Open)*
- H2G-6977 — Employee Screen - Error Handling for Onboarding Documents *(Open)*
- H2G-6978 — Corporation Screen - Error Handling for Onboarding Documents *(Open)*
- H2G-6997 — [Onboarding] Create dbo.OnboardingMappingMessage table and read endpoint *(Ready to Deploy)*

### Downstream: sending onboarding data to Payroll (OnboardingImportProcessor, EmployeeSetupProcessor) (15)

- H2G-6694 — Employee Onboarding Create (Valid / Draft) Status in HTG and Payroll *(Open)*
- H2G-6695 — Deal Memo Onboarding Create (Valid / Draft) Status in HTG and Payroll *(Open)*
- H2G-6922 — Corporation Onboarding Create (Valid / Draft) Status in HTG and Payroll *(Open)*
- H2G-6949 — [OnboardingImportProcessor] Scaffold HTGPlus.OnboardingImportProcessor (Kafka host, DI, topics config) *(Ready to Deploy)*
- H2G-6950 — [OnboardingImportProcessor] Negotiate Payroll contract shape for Onboarding bundle + individual entity sends *(Ready to Deploy)*
- H2G-6952 — [EmployeeSetupProcessor] Build Onboarding bundle Avro contract + OnboardingPackageSaveHandler/Service *(Ready For Dev)*
- H2G-6953 — [EmployeeSetupProcessor] Build combined response handler for the Onboarding Payroll bundle reply *(Ready For Dev)*
- H2G-6954 — [EmployeeSetupProcessor] Include Valid/Draft status in Onboarding bundle payload + sync Draft to Valid transition to Payroll *(Open)*
- H2G-6955 — [OnboardingImportProcessor] Support partial/blank-field sends in the Onboarding bundle payload builder *(Open)*
- H2G-6958 — [OnboardingImportProcessor] Build Employee/DealMemo SavedHandlers + response handlers *(Ready For Dev)*
- H2G-6959 — [OnboardingImportProcessor] Register OnboardingImportProcessor in KnownPayrollTopics + wire shared retry/DLQ router *(Refinement)*
- H2G-6960 — [EmployeeSetupProcessor] Onboarding-triggered Corporation edits: Create a new CorporationProcessor pipeline *(Open)*
- H2G-6961 — [OnboardingImportProcessor] Add logging/monitoring parity and deploy pipeline entry *(Ready to Deploy)*
- H2G-6963 — [EmployeeSetupProcessor] SPIKE Define and scope ongoing sync of Payroll-initiated changes back to Employee/DealMemo/Corporation *(Open)*
- H2G-7186 — [EmployeeSetupProcessor] Send Employee/DealMemo saves to Payroll as Avro messages *(Open)*

### Enablers: EmployeeSetupCore, Scheduler.Core removal and shared infrastructure (13)

- H2G-6220 — EmployeeSetupCore: move DealMemo entity out of Scheduler.Core *(Ready to Deploy)*
- H2G-6393 — EmployeeSetupCore: move Employee entity out of Scheduler.Core *(Ready to Deploy)*
- H2G-6882 — Migrate employee domain types into HTGPlus.EmployeeSetupCore *(Ready to Deploy)*
- H2G-6920 — Employee: Unable to create employee in QA *(Deployed)*
- H2G-6979 — Migrate Employee Payroll operations from PayrollDataServiceClient to IPayrollApiClient *(Ready to Deploy)*
- H2G-6980 — Move EmployeeProvider and EmployeeMapper into HTGPlus.EmployeeSetupCore *(Ready to Deploy)*
- H2G-6986 — Consolidate shared type converters into HTGPlusExtensions field declaration *(Ready to Deploy)*
- H2G-7053 — Simplify Kafka Avro serializer registration *(Open)*
- H2G-7054 — Migrate employee Kafka publishing to the generic data sync contract *(Open)*
- H2G-7087 — Move DealMemo provider files to HTGPlus.EmployeeSetupCore *(In Code Review)*
- H2G-7145 — Employees - Employee.Store() fails after loading by id because Employee_GetById does not return DbCode *(Ready For Dev)*
- H2G-7166 — [CorporationCore] Remove the HTGPlus.Business.Scheduler.Core dependency *(Ready to Deploy)*
- H2G-7216 — Make DateTimeEx.UtcOffset execution-local *(Open)*

### File tracking (LOG-TRACK) in other components (7)

- H2G-7173 — LOG-TRACK: [HTGPlus.ExcelMapping] Extract FileTrackerProvider and FileTrackingStatus into a shared tracking library *(In Design)*
- H2G-7179 — LOG-TRACK: [HTGPlus.ExcelMapping.API] Support onboarding packages in file tracking API *(Open)*
- H2G-7180 — LOG-TRACK: [HTGPlus.ExcelMapping.API] Support onboarding payloads in AI Data endpoints *(Open)*
- H2G-7181 — LOG-TRACK: [HTGPlus.Web.UI] Process File Tracking: package type filter, onboarding statuses and per-employee detail *(Open)*
- H2G-7182 — LOG-TRACK: [HTGPlus.Web.UI] AI Data Query: onboarding extraction viewer *(Open)*
- H2G-7183 — LOG-TRACK: [Docs] Document onboarding tracking markers and Kibana queries *(Open)*
- H2G-7189 — Ingestion packages search: expose onboarding document Ids and counts per package *(Abandoned)*

### Onboarding Dashboard and onboarding screens (HTG UI) (23)

- H2G-6623 — [Groomed] Document Viewer Popup — Reusable component for viewing and updating document status *(Ready to Deploy)*
- H2G-6905 — HTGPlus Web UI — Code Standardization & Modernization Initiative *(Abandoned)*
- H2G-6967 — Onboarding Dashboard – Result table (Design, Data Integration & fucntionality) *(Refinement)*
- H2G-6968 — Onboarding Dashboard – Functionality - Search Section *(Abandoned)*
- H2G-6969 — Onboarding Status Doughnut Chart *(In Dev)*
- H2G-6970 — Onboarding Dashboard Document Routing & Lifecycle *(Open)*
- H2G-6971 — Onboarding Documents - On click count of (#EMP, #DM, #Corp) Expect to redirect to respective maintenance *(Open)*
- H2G-6972 — Deal Memo Screen - New Filter in Search & Grid View ( Doc View) *(Open)*
- H2G-6975 — Employee Screen - New Filter in Search & Grid View ( Doc View) *(Open)*
- H2G-6976 — CoorporationScreen - New Filter in Search & Grid View ( Doc View) *(Open)*
- H2G-7066 — Onboarding Dashboard -  Implement PDF Viewer as a Draggable Popup Window - File Name column in Dashboard *(Open)*
- H2G-7069 — Doughnut Chart - Functionality *(Open)*
- H2G-7076 — Standardized MultiSelect component (shared/components) with Storybook *(Ready for Code Review)*
- H2G-7079 — Onboarding Dashboard – Document Assignment and Status functionality *(Open)*
- H2G-7195 — Onboarding Dashboard – Refresh / SYNC Icon functionality *(Open)*
- H2G-7196 — Onboarding Dashboard – Onboarding Dashboard Initial State & Post-Go-Live Documents *(Open)*
- H2G-7197 — Onboarding Documents - Miscellaneous-Only Documents *(Open)*
- H2G-7204 — Additional Search filters (Employee, Deal memo, Corporation) Screen - Supporting Onboarding Documents *(Open)*
- H2G-7205 — Onboarding Documents Dashboard Navigation to Deal Memo/ Employee/ Corporation Screen- Search Section Prepopulate and apply filter *(Open)*
- H2G-7206 — Result Table (EMP / DM/ CORP) - Implement PDF Viewer as a Draggable Popup Window *(Open)*
- H2G-7210 — Deal Memo Documents Sub-Tab / Document History - Design + Data Integration+ Functionality *(Open)*
- H2G-7211 — Employee Documents Sub-Tab / Document History - Design + Data Integration+ Functionality *(Open)*
- H2G-7212 — Corporation Documents Sub-Tab / Document History - Design + Data Integration+ Functionality *(Open)*

### HTGPlus.OnboardingImportClean (out of scope of this inventory) (2)

- H2G-6999 — Onboarding Import Clean (Clean Architecture) — reference implementation *(Ready to Deploy)*
- H2G-7178 — LOG-TRACK: [HTGPlus.OnboardingImportClean] Emit tracking events per onboarding set (ON HOLD) *(Open)*

### Not onboarding: timecard and other work under the same epic (6)

- H2G-5205 — send timecard data to AI COE *(Deployed)*
- H2G-5782 — 03187291: Support Unstructured for CAPS Excel processing *(Released)*
- H2G-5944 — Service to consume Kafka and create RTCE timecard. *(Ready to Deploy)*
- H2G-6237 — Timecard Processor - refactor *(Ready to Deploy)*
- H2G-6360 — fix mock payload *(Ready to Deploy)*
- H2G-6568 — PayrollRouter — limit retry to transient infrastructure errors and business-signaled retryable errors *(Ready to Deploy)*
