# H2G-6532 — Re-architect Onboarding Employee/DealMemo subrecord persistence as separate linked ORM documents

Source: Jira ticket H2G-6532 (type Story, status "Released", created 2026-08-04, updated 2026-09-25). Split off from H2G-6424
during manager review as follow-on work that does not block that PR.

## Problem

`HTGPlus.OnboardingImport` persisted employee subrecords (i9Statuses, directDeposits, taxWaivers, permanentDeductions) and deal
memo subrecords (guarantees with inclusions, allowances) as nested JSON arrays inside the parent document's CustomFields. The
real HTGPlus system reads them from separate linked tables: EmployeeSubRecord (through `EmployeeSubRecord_GetByEmployee`) and
DealMemoGuarantee / DealMemoSubRecord (through `ParentExternalId`). As implemented, the onboarding subrecords would save but
show empty on the employee and deal memo management screens.

## Scope

- Employee subrecords: persist each as its own EmployeeSubRecord document via `Document.Store()`, linked by Code/DbCode to the
  employee. This needs new `EmployeeSubRecord_Insert` / `_Update` stored procedures, because only `EmployeeSubRecord_Upsert`
  exists and the generic ObjectSchema ORM engine only looks for `{TableName}_Insert` / `{TableName}_Update`.
- Deal memo guarantees (and inclusions): persist each as its own DealMemoGuarantee document via `Document.Store()`, linked by
  the DealMemoId (Guid) foreign key. The procs exist, but DealMemoGuarantee inherits extra `[Field]` properties from the base
  DealMemo class that the procs do not declare as parameters; this risks a "too many arguments" SQL error and must be tested.
- Deal memo allowances: same pattern through DealMemoSubRecord, with the same inherited-field risk.
- Confirm the nested-versus-flat guarantees/allowances contract question with Data Extract (H2G-6389, task 1) before
  finalizing the linking shape.
