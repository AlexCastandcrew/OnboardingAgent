# H2G-6950 — Payroll Contract Negotiation Proposal (Onboarding Bundle)

Source: Confluence page 5625872388, space HPS, last modified oct 01, 2026.

# H2G-6950 — Negotiate Payroll Contract Shape for Onboarding Bundle + Individual Entity Sends

**Ticket:** [H2G-6950](https://castandcrew.atlassian.net/browse/H2G-6950)
**Status:** draft proposal — not yet reviewed by the team lead or Payroll
**Branch:** `H2G-6950-payroll-contract-negotiation` (worktree `htg-server-h2g-6950`, off `majorfeature_2.0`)

## Why this doc exists

H2G-6950 is a cross-team dependency, not a build ticket: it asks HTG to negotiate three things with the Payroll team before the "Sending to Payroll" work (design doc §7, this page's parent) can be built:

1. The bundle payload shape (Employee+DealMemo+Corporation together)
2. The shape of Payroll's single combined reply to that bundle
3. Whether individual entity sends (Employee/DealMemo/Corporation edited standalone after onboarding) reuse Payroll's existing per-document Kafka pipelines

Per this page's parent, Dependencies & Risks section: Payroll has not committed a timeline for the bundle concept (a similar bundle ask for CLE+Producer+ProdCo+Project is also still awaiting a reply), and Payroll's own per-document pipelines are themselves still in development. This proposal is meant to give Payroll something concrete to react to, grounded in what HTG's side already does today — not a request built on assumptions.

## What HTG's side actually does today (verified against code, 2026-09-26; re-verified 2026-10-01 — see Update section below)

**Employee/DealMemo → Payroll is REST, not Kafka.** There is no existing Employee or DealMemo Kafka producer. Today's sync goes through `PayrollDataServiceClient` (`HTGPlus.Business.Scheduler.Core/DataService/PayrollDataServiceClient.cs`), invoked from `DealMemoServiceProvider.RequestPayrollService`/`CreatePayrollPayloadFromDealMemo` (`HTGPlus.Business.Providers/Dealmemo/DealMemoServiceProvider.cs`) — a data-service call, one deal memo at a time. **This matters for question 3**: "reuse Payroll's existing per-document Kafka pipelines" doesn't fully apply to Employee/DealMemo today, because there is no existing Kafka pipeline for them to reuse. Either Payroll needs to build one, or HTG's individual-entity-send path needs a new Kafka producer for Employee/DealMemo before that reuse question is even answerable.

**Corporation → Payroll already has a full Kafka round-trip**, and is the closest existing precedent:

- Producer: `CorporationSavedHandler` (`HTGPlus.CorporationProcessor/Handlers/CorporationSavedHandler.cs`) → `ICorporationSavedProcessor` → `CorporationRequestBuilder` (`HTGPlus.CorporationProcessor/Services/CorporationRequestBuilder.cs`), which builds the Avro contract class `ImportCorporation` (`Cnc.Payroll.Avro`) and produces one message per corporation to topic alias `OutImportCorporation`. Strictly per-entity — sub-records (registrations) are nested inside the same message as an `importCorpRegistrations` array, not sent separately.
- Reply: `CorporationResponseHandler` (`HTGPlus.CorporationProcessor/Handlers/CorporationResponseHandler.cs`) consumes `CorporationValidationMessage` off `InCorporationResponse`, correlated back via `htgRequestId` (a Guid HTG generates and sends outbound, mirroring `document.RequestId`). On success it persists `PrPrimaryKey` from `response.crudResults[].primaryKey`; on failure it builds field-level validation messages and posts a UI notification.

**This `SavedHandler` → `RequestBuilder` → `ResponseHandler` shape is a repeated convention** across `HTGPlus.CorporationProcessor`, `ProducerProcessor`, `ProductionCompanyProcessor`, `ClientProcessor`, `AgentProcessor`, `LookupEntityProcessor`, `ProjectSetupProcessor` — no shared base class, each `*Processor` reimplements it, but it's consistent enough to be worth naming to Payroll as "the shape we already use." Employee and DealMemo are not on this pattern yet.

**Corporation has zero coverage for the bundle path specifically.** `OnboardingCorporationProvider` (`HTGPlus.OnboardingImport/Ingestion/Providers/OnboardingCorporationProvider.cs`) already persists Corporation data arriving via onboarding ingestion (upsert via `Document.Store`, same `TransformKeyToCOMN` key as `ImportCorporationHandler`) — but it only handles HTG-side persistence, it does not send anything to Payroll. The only Corporation→Payroll producer today is the classic UI-save flow (`CorporationSavedHandler`), not the onboarding/bundle path. Confirms the design doc's claim: a new send path is required, though it can reuse `OnboardingCorporationProvider` for persistence and mirror `CorporationRequestBuilder`'s Avro-building logic for the outbound send.

**A bundle precedent already exists, inbound.** `OnboardingMessage` (`HTGPlus.OnboardingImport/Ingestion/Models/OnboardingMessage.cs`) — the contract HTG receives from the Data Extract/AiCOE side — carries a `PackageId` plus a `List<OnboardingSet>`, where each `OnboardingSet` bundles one optional `Corporation`, one `Employee`, and one `DealMemo` under a shared `ReferenceId` (the idempotency key). This is directly useful framing for Payroll: HTG already receives Employee+DealMemo+Corporation as one bundled unit; this negotiation is about the mirror-image outbound shape.

## Proposed talking points for Payroll (draft — needs the team lead's sign-off before sending)

1. **Bundle payload shape**: propose one Avro message per `OnboardingSet.ReferenceId`, structured like `ImportCorporation` today but extended with sibling `Employee`/`DealMemo` sub-payloads under the same message, each carrying its own `htgRequestId` for correlation — reusing the existing Corporation Avro contract as the base rather than inventing a new shape from nothing.
2. **Combined reply shape**: propose Payroll's single reply mirror `CorporationValidationMessage`'s `crudResults` array, but keyed per entity type (corporation/employee/dealMemo) so each can report its own `PrPrimaryKey`/validation errors independently within one message — avoids a full-bundle fail-or-succeed reply when only one entity in the set has a problem.
3. **Individual-send reuse**: honest framing for Payroll — Corporation can reuse its existing pipeline as-is; Employee and DealMemo currently have no Kafka pipeline on HTG's side at all (REST only today), so "reuse Payroll's existing per-document pipeline" is only answerable for Corporation right now. Building Employee/DealMemo Kafka producers, if needed, is new HTG-side scope not yet ticketed — worth surfacing to the team lead as a probable follow-on ticket rather than assuming it's free.

## Risks / open dependencies (from the parent page, unchanged by this research)

- Payroll has no committed timeline for the bundle concept; a similar bundle ask (CLE+Producer+ProdCo+Project) is still awaiting a reply.
- Payroll's own per-document pipelines are themselves still in development.
- HTG can build and unit-test its own side independent of Payroll's timeline, but the feature isn't complete end-to-end until Payroll's side is ready — outside HTG's control.

## Recommended next steps

1. Get this document reviewed by the team lead before it goes to Payroll (matches this team's design-doc pattern from H2G-6838).
2. Once agreed internally, take points 1–3 above to Payroll's team lead as the actual negotiation.
3. Do not start building the bundle producer/response handler until Payroll confirms a shape — the branch `H2G-6950-payroll-contract-negotiation` exists to hold this document (and, if useful later, an illustrative DTO sketch), not production integration code, until that confirmation lands.

## Update — 2026-10-01 (re-verified against current code, HEAD `60d1fa1ba8`)

Re-checked every claim above after rebasing onto the latest `majorfeature_2.0` (61 new commits since this doc was first written). **All original findings still hold**: Employee/DealMemo have no real Kafka producer to Payroll yet, Corporation's `CorporationRequestBuilder`/`CorporationResponseHandler` round-trip is unchanged (only a mechanical `Types.corporation` → `.ToString()` signature touch from an unrelated ticket), and no outbound bundle-send code exists anywhere in the repo.

**New since this proposal was first written:** a skeleton project `HTGPlus.EmployeeSetupProcessor` was merged (commit `f9f4385aa2`, ticket **H2G-6949**, "Generate EmployeeSetupProcessor") — project scaffold only (`Configuration.cs`, `EmployeeSetupKafkaTopics.cs`, `EmployeeSetupProcessorTopics.cs`, `Program.cs`), no `Handlers/`/`Services/`/`Interfaces/` yet, no topic aliases defined, nothing registered in DI. Its own code comments say, verbatim: *"Consumer and producer aliases will be added here as handlers are implemented in subsequent tickets (H2G-6950 and onwards)"* — and it already defines a `ShadowMode` flag (currently `true`) for a safe production cutover.

**Why this matters for the negotiation:** whoever filed H2G-6949 already anticipated that this negotiation would conclude with a new Employee→Payroll Kafka pipeline mirroring Corporation's shape, and pre-built the project skeleton plus a shadow-mode cutover plan for it. Two things worth raising with the team lead before this goes further:

1. The "does Employee get a Kafka pipeline" half of talking point 3 may already have an internally agreed direction (per H2G-6949's own comments) rather than being fully open — worth confirming explicitly instead of re-deciding it.
2. Whatever gets negotiated with Payroll for Employee should account for the shadow-mode cutover plan already scaffolded, so the new pipeline can be validated against real Payroll traffic before fully replacing the REST path.
