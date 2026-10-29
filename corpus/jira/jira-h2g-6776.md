# H2G-6776 — Onboarding Mapping Service: consume AI-extracted onboarding data and produce Employee records for OnboardingImport

Source: Jira ticket H2G-6776 (type Spike, status "Deployed", created 2026-09-03, updated 2026-10-10).

## Requirement

Transform the raw AiCOE onboarding package JSON (A) into the employee `customFieldData` JSON (B) that `OnboardingImport`
consumes.

- **A (input):** the AiCOE onboarding extraction message. Package-level fields are `dbCode`, `productionCompany`,
  `projectNumber` and `extractedAt`. It holds `employees[]`, each with `employeeIdentity` and `forms[]` (Start/Close, I-9,
  W-4, State W-4); each form is a flat array of `{ field, value }`.
- **B (output):** the `customFieldData` object matching the extension-data shape of
  `HTGPlus.OnboardingImport.Ingestion.Models.OnboardingEmployee`, keyed by the field names registered on the `employee`
  document schema.

Before assembling B, generate a new GUID per employee (a new field) to be used as the Employee document's own `Id` when it is
persisted downstream, and include it in B.

## Where to find the rules

- Field-by-field translation rules (checkbox to HTG code, defaults, hardcodes): H2G-6738.
- Field list and types, the source of truth for B: `DocumentSchemas.cs`, `Employees.Initialize()` in `HTGPlus.DocumentCore`.
- Source-mapping pattern: `ProjectRequestV2SchemaExtensions.AddV2` in `HTGPlus.ProjectCore`.
- Generic schema-walk and type-coercion pattern: `DocumentCustomFields.Apply` / `Fixup` in `HTGPlus.DocumentCore`.
- Consumer shell: `OnboardingPackageHandler` in `HTGPlus.ExternalDataMapper` (H2G-6624).

## Acceptance criteria

Given a sample A, the output B matches the expected employee `customFieldData` shape, with fields translated per the H2G-6738
rules, and includes the newly generated GUID.
