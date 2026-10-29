# Onboarding Import — Remaining Work & Dependencies

Source: Confluence page 5523996673, space HPS, last modified sept 04, 2026.

## Sizing

This is sized as a T-shirt **XL** (revised up from Large). Two things changed since the Large estimate: Defaults and Document Validation (sections 5 and 6) are real, ticketed scope that wasn't accounted for when Large was first sized, and business has now confirmed **complex filtering** for the Dashboard (section 8) — the costlier of the two paths under consideration. Between those two changes and the still-open dependencies below (Payroll's bundle timeline not committed, AiCOE contract not yet validated against real data, and the open grooming items below), a precise point estimate isn't meaningful, but this comfortably clears Large.

**Caveat on Dashboard sizing:** the Onboarding tab is additive to the existing Documents dashboard, not a standalone build — current design reuses that dashboard's grid, filter bar, pagination, and summary donut rather than building new infrastructure from scratch. If that reuse holds up (see the two open items in section 8), Dashboard's actual cost is meaningfully smaller than a from-scratch complex-filtering build, and the overall size could move back toward Large. Not yet confirmed — XL stands until it is.

## 1. Technical foundation cleanup (required for this initiative)

Employee and DealMemo data currently live bundled together with several unrelated systems (Contracts, Batches, Invoices, Timecards) in one shared piece of code (`HTGPlus.Business.Scheduler.Core`). Building the new Onboarding service directly on top of that means any change made for Onboarding carries risk of accidentally breaking those unrelated systems. This work separates Employee and DealMemo into their own dedicated module (`HTGPlus.EmployeeSetupCore`) before Onboarding builds on top of them — this reduces the risk of Onboarding development breaking existing, unrelated functionality, and makes both easier to maintain going forward.

- Move the Employee data model and its schema registration into `HTGPlus.EmployeeSetupCore`, update everywhere that references it — [H2G-6393](https://castandcrew.atlassian.net/browse/H2G-6393)
- Move the DealMemo data model into `HTGPlus.EmployeeSetupCore`, update everywhere that references it — [H2G-6220](https://castandcrew.atlassian.net/browse/H2G-6220)
- Full regression testing across every existing system that touches Employee/DealMemo, to confirm nothing broke

## 2. Existing backlog (already in Jira)

- H2G-6653 — mapping schema single-source-of-truth (Ready for QA)
- H2G-6697 — Employee onboarding payload renders correctly in UI (In Dev)
- H2G-6698 — DealMemo onboarding payload renders correctly in UI (not started)
- H2G-6533 — decide + implement retry/DLQ handling
- H2G-6647 — fix: partial writes survive failure / duplicate risk on redelivery
- ~~H2G-6651 — dedupe for re-onboarding~~ — **Abandoned (business decision):** duplicate DealMemo/subrecord rows on re-onboarding are acceptable; business will manually delete them. No dedupe logic will be built for DealMemo or its subrecords.
- H2G-6654 — spike: can transaction scoping move out of handler orchestration
- H2G-6676 — fix: DealMemoGuarantee/SubRecord TBase=Document field gap

## 3. Corporation persistence (new — currently zero coverage)

- Confirm the JSON contract carries Corporation data; coordinate with AiCOE/Data Extract if it doesn't
- Build a new `OnboardingCorporationProvider` — persist-only, same pattern as the existing Employee/DealMemo providers
- Wire Corporation into the materializer flow, ordered before Employee (Employee's `taxIdNumber` FKs to it)
- Unit tests

## 4. Mapping layer

Field-level requirements confirmed directly against the business tickets ([H2G-6738](https://castandcrew.atlassian.net/browse/H2G-6738) — Employee, [H2G-6739](https://castandcrew.atlassian.net/browse/H2G-6739) — Deal Memo). Only building tables the tickets actually call for — not assuming a table is needed just because a field is external.

Implemented as a new, shared mapping service — `HTGPlus.ExternalDataMapper` ([H2G-6624](https://castandcrew.atlassian.net/browse/H2G-6624)) — separate from OnboardingImport itself, and not onboarding-specific: it's designed to take on other external-data mapping needs later (e.g. timecards). The service's consumer shell is built; the field-mapping logic itself is not yet implemented. It consumes the AiCOE onboarding-forms Kafka message, maps values, and produces a message in OnboardingImport's existing contract shape so OnboardingImport requires no changes.

Tracked in Jira: [H2G-6776](https://castandcrew.atlassian.net/browse/H2G-6776) (Employee mapping — includes a new identifier generated per employee, intended to become the Employee record's own ID once persisted), [H2G-6777](https://castandcrew.atlassian.net/browse/H2G-6777) (Deal Memo hard-code/passthrough fields), [H2G-6778](https://castandcrew.atlassian.net/browse/H2G-6778) (new Occupation Code mapping table), [H2G-6786](https://castandcrew.atlassian.net/browse/H2G-6786) (resolving external field values to real HTG records, with an error-tracking mechanism for values that can't be resolved). Implementation detail lives on those tickets, not here.

- **Mapping engine decided: RuleCore.** Genuinely new concepts get a new RuleCore table with its own independent dataset (Occupation Code — [H2G-6778](https://castandcrew.atlassian.net/browse/H2G-6778)); fields with existing shared mapping data (Union, State, Work Schedule, etc.) reuse that same data via RuleCore's own copy of the table, not a new dataset and not a direct reference to the heavier engine those tables originally lived in.
- **City, Hire State, and Work State are passthrough — no mapping table involved at all, new or reused.** AiCOE has direct access to HTG's reference tables and resolves these via ML before sending; the mapping service does not do any lookup/matching for them. (Hire/Work State is our proposed resolution, not yet business-confirmed — see Open Items to Groom below.)
- Verify current behavior when a field has no mapping today — safe-blank vs. throws vs. raw passthrough (investigation spike)
- Wire up reuse of the existing union mapping table (via RuleCore) for pension/vacation/work-dues union fields (per ticket: "use mapped value from existing mapping table")
- Build new mapping table: Occupation Code (per ticket: "use mapped value from new mapping table Data Mapping Occupation Code") — [H2G-6778](https://castandcrew.atlassian.net/browse/H2G-6778)
- Build new mapping table(s): Contract Codes (pension + HTG contract)
- Build the "leave blank + flag" intervention mechanism — matches the existing `IngestionMapper`/`interventionRequired` precedent (leave the field unmapped, flag the record for review, never drop it) — but build a **new table scoped to Onboarding**; do not reuse the timecard `Messages` table (it's timecard-specific and already large)
- Employee-side fields (Gender, Ethnic Code, I-9 Status, etc.) are simple fixed-value translation per the ticket ("hard code: one-to-one match" from a small set of checkbox options) — these do not need the fuzzy mapping-table engine, a straightforward lookup is sufficient and correctly scoped smaller than the Deal Memo free-text fields — [H2G-6776](https://castandcrew.atlassian.net/browse/H2G-6776)

**Not building:**

- ~~WCI Code mapping table~~ — confirmed derivation-only from Occupation Code, independently by both H2G-6739 and the "HTG Data Mapping" design page; not building a separate table for this.

## 5. Defaults (new — currently missing from scope entirely)

Business has already fully specified this ([H2G-6691](https://castandcrew.atlassian.net/browse/H2G-6691) — Employee, [H2G-6692](https://castandcrew.atlassian.net/browse/H2G-6692) — Deal Memo). Not a small item — covers multiple subrecords and real conditional logic, not just flat default values.

- Apply Employee create defaults (EE Valid, Lockout, Address country, Resident Status, etc.) only when the source document didn't already provide a value
- Apply Employee subrecord defaults: I-9, Federal W-4, State W-4, Direct Deposit, Alternate Address, Emergency Contacts, TD1 (Canada)
- Apply Deal Memo create defaults (Status, Tax ID from selected Employee, Hire/Effective Date, Normal Daily Hours, Agent Authorization, etc.)
- Apply Deal Memo subrecord defaults: Studio Guarantee, Distant Guarantee, Other Guarantee
- Implement the conditional union/contract auto-fill logic: if Pension Union is received and there's exactly one associated contract, auto-fill all related contract fields; if multiple contracts exist, require manual user selection instead
- Implement project-derived defaults (Work Dues checkbox, Tax Pay Frequency fallback, ACA Status Code, Workers Comp self-insured, Season, Worker Type from occupation, PAYPA Viacom coding fields)

## 6. Document validation (new — currently missing from scope entirely)

Determines whether a submitted document is acceptable, and drives the Valid/Draft decision below. Source tickets: [H2G-6730](https://castandcrew.atlassian.net/browse/H2G-6730) — Employee + Corporation, [H2G-6731](https://castandcrew.atlassian.net/browse/H2G-6731) — Deal Memo. Not related to dedupe — this is purely about whether a document's content is acceptable before its data is used.

- Implement signature + date presence checks per document type — not authenticity, just confirming something is entered
- Implement per-document required-field checks: I-9 (List A, or List B + List C, with expiration-vs-signed-date and first-date-of-employment checks), Fed W-4 (current year, with a grace period accepting prior-year forms through February 15), State Tax Form, W-9
- Implement new-record failure behavior: save all available fields, set the record to Draft, inform the user
- Implement existing-record failure behavior: reject the update outright, do not modify the existing record, inform the user
- **Grooming needed:** I-9 List A/B/C document classification doesn't exist anywhere in HTG today — confirm whether AiCOE's extraction already classifies which list a submitted document belongs to, or whether HTG needs to build that classification itself
- **Grooming needed:** failure behavior is undefined (marked TBD in the tickets) for Direct Deposit, Union Dues, 401K, and Minor Trust (H2G-6730), and Agent Auth and Union Dues (H2G-6731)

## 7. Sending to Payroll (Corporation + Employee + DealMemo bundle)

- Negotiate the bundle contract shape with the Payroll team
- Wire Corporation into Onboarding's create chain for the bundle path
- Build the outbound Kafka producer for the bundled package
- Build the combined response handler (parses Payroll's single reply, updates all 3 entities' keys)
- Support partial/blank-field sends in the payload builder
- **Include Valid/Draft as an explicit status field in the bundle payload, not just "some fields are blank."** Per [H2G-6694](https://castandcrew.atlassian.net/browse/H2G-6694)/[H2G-6695](https://castandcrew.atlassian.net/browse/H2G-6695), the system must set a real Valid/Draft status, communicate it to both HTG and Payroll, and keep it consistent between the two — including later, when a Draft is completed and transitions to Valid. That transition needs its own sync back to Payroll, not just the initial send.
- Verify the existing Employee/DealMemo provider path correctly handles the post-fix resubmit, including the Draft→Valid status transition above (verification, plus possibly new work if that transition isn't already handled)
- The new-record vs. existing-record validation behavior from the Document Validation section above (section 6) directly determines when a record becomes Draft vs. Valid before this bundle send

## 8. Dashboard

Tracked against [H2G-6623](https://castandcrew.atlassian.net/browse/H2G-6623) (Document Viewer Popup), [H2G-6757](https://castandcrew.atlassian.net/browse/H2G-6757), and [H2G-6748](https://castandcrew.atlassian.net/browse/H2G-6748).

**New — a dedicated Onboarding tab, additive to the existing Documents dashboard, not a replacement.** Current design reuses the Documents dashboard's grid, filter bar, pagination, and summary donut rather than building new dashboard infrastructure from scratch — this meaningfully reduces scope versus a standalone build, but depends on two things not yet confirmed:

- Whether the Documents dashboard's grid/filter/donut are genuinely shared, reusable components — not just visually similar, separately-built ones
- Whether the underlying data store already tags each document by type (Deal Memo/Employee/Corporation/Misc) — needed for the group-level rollup counts below

Assuming reuse holds:

- Add a new Onboarding tab, pointed at onboarding-scoped data using the existing grid/filter/pagination components
- Add a "By Coordinator" summary list — a new widget, a simple grouped count (the summary donut itself already exists on the Documents dashboard and is reused as-is)
- Add a "Doc Status" filter, and reconcile the onboarding-specific status vocabulary (Pending Review / Hold / Complete) against the status set already used elsewhere on the Documents dashboard (New / Assigned / InProgress / ReAssigned) — same status field extended, or a new one scoped to onboarding docs
- Add group-level rollup counts by document type and status (e.g. "38 DM 112 Emp / 12 Corp 3 Misc", "+159 Pending +6 Hold") — the current dashboard's group rows show only a flat document count, no type/status breakdown
- Add a 30-day default display window (already confirmed with business) — the current dashboard shows full history with no time cutoff; onboarding needs older records auto-dropped from view
- Integrate file access with the Document Viewer Popup component (H2G-6623)
- Build cross-page linking from the dashboard to the Employee and Deal Memo pages — **note: this is distinct from** [**H2G-6632**](https://castandcrew.atlassian.net/browse/H2G-6632) **(zoom-in from Deal Memo's Tax ID field to the Corporation screen), which is a separate, already-open, maintenance-screen-to-screen navigation item, not part of the dashboard**
- Per-document Ingestion Status (Error/Submitted/Pending) is not dashboard-side work — it depends on data produced by H2G-6786 (error table) and H2G-6533 (retry/DLQ), both tracked above. Once that data exists, surfacing it here is a small addition.
- Exact filtering/statuses/fields still unscoped in some areas — pending confirmation of the two open items above

## Dependencies & Risks

**Upstream — AiCOE / Data Extract team**

- No real onboarding data has been received yet. All mapping and persistence work is built against a mock payload HTG sent to AiCOE.
- AiCOE has not confirmed they'll conform to that mock shape — the contract could still change once they build their real extraction.
- Risk: rework on the Employee/DealMemo/Corporation models and the mapping layer if the real contract differs from the mock.

**Downstream — Payroll**

- Payroll does not have the bundle concept built, and hasn't committed to a timeline for it (a bundle ask for a different chain — CLE+Producer+ProdCo+Project — is still awaiting a reply).
- Payroll's individual per-document Kafka pipelines are themselves still in development, not finished.
- HTG can build and unit-test its own side of the Payroll-bundle work independent of this, but the feature isn't complete end-to-end until Payroll's side catches up — that timeline is outside HTG's control and isn't reflected in this task list.

**Internal — existing Employee/DealMemo/Corporation maintenance screens are unproven at real usage**

- These screens already shipped, but have had very little real production traffic since release — bugs and usability issues likely haven't surfaced yet, not because the screens are solid, but because almost no one has been using them.
- Once Onboarding drives real business users into these screens regularly (reviewing and correcting onboarding-created records), that traffic will likely surface both latent bugs and usability gaps — these screens weren't originally designed around an onboarding review-and-correct workflow.
- Can't be concretely scoped yet — the actual issues won't be known until real usage happens. Plan for a follow-on round of UI stabilization/usability work once Onboarding goes live, rather than assuming these screens are done just because they already shipped.
- Separately, a small number of already-open, narrowly-scoped UI tickets exist and are worth tracking alongside this: [H2G-6629](https://castandcrew.atlassian.net/browse/H2G-6629) (hotkey toggle, Employee screen), [H2G-6630](https://castandcrew.atlassian.net/browse/H2G-6630) (paycode column, Deal Memo), [H2G-6631](https://castandcrew.atlassian.net/browse/H2G-6631) (navigation warning block, Deal Memo), [H2G-6632](https://castandcrew.atlassian.net/browse/H2G-6632) (zoom-in, Deal Memo → Corporation), [H2G-1314](https://castandcrew.atlassian.net/browse/H2G-1314). These are specific, pre-existing asks — not a substitute for the general "unknown issues from new traffic" risk above.

## Open Items to Groom with Business

Not resolved by assumption — these need a real grooming conversation before being scoped as work:

- **Hire State / Work State — proposed resolution: passthrough from AiCOE, no mapping table.** H2G-6739 flags a possible need ("Use Data Mapping State?") but doesn't confirm it either way. Current design treats these the same as City — AiCOE resolves the value directly and sends it resolved — but this still needs business/AiCOE confirmation before being treated as final.
- **I-9 List A/B/C classification and the remaining TBD document types** — see section 6 (Document validation) for detail.
- **Misc document types** (e.g. PGA form, agent, 401k, work dues) — per the business onboarding tracking page, these need warning displays on the Employee/Deal Memo screens and a corresponding label on the dashboard. No ticket or detailed requirements exist yet.
- **Document separations** — appears as its own scope item on the business onboarding tracking page with no requirements defined yet. Not otherwise represented in this document.
- **Dashboard reuse assumptions** (section 8) — whether the Documents dashboard's components are genuinely shared/reusable, and whether documents are already tagged by type in the underlying data store. Confirming these could move the overall sizing back toward Large.

## All business tickets — grooming status

Every business-originated ticket referenced anywhere in this document. Status is set to **Need groom** for all of them as a starting point — update individually once each is actually reviewed with business.

| Ticket | Status |
| --- | --- |
| [H2G-6627](https://castandcrew.atlassian.net/browse/H2G-6627) — Onboarding Documents (Employee & Dealmemo) via AI into HTG (epic) | Need groom |
| [H2G-6628](https://castandcrew.atlassian.net/browse/H2G-6628) — Employee & Deal memo Improvements (epic) | Need groom |
| [H2G-2950](https://castandcrew.atlassian.net/browse/H2G-2950) — Zoom in functionality in HTG (epic) | Need groom |
| [H2G-6623](https://castandcrew.atlassian.net/browse/H2G-6623) — Document Viewer Popup | Need groom |
| [H2G-6629](https://castandcrew.atlassian.net/browse/H2G-6629) — Hotkey toggle, Employee screen | Need groom |
| [H2G-6630](https://castandcrew.atlassian.net/browse/H2G-6630) — Paycode column, Deal Memo | Need groom |
| [H2G-6631](https://castandcrew.atlassian.net/browse/H2G-6631) — Navigation warning block, Deal Memo | Need groom |
| [H2G-6632](https://castandcrew.atlassian.net/browse/H2G-6632) — Zoom-in, Deal Memo → Corporation | Need groom |
| [H2G-1314](https://castandcrew.atlassian.net/browse/H2G-1314) — Display layer (Employee/DM/Corp usability) | Need groom |
| [H2G-6691](https://castandcrew.atlassian.net/browse/H2G-6691) — Defaults, Employee Maintenance | Need groom |
| [H2G-6692](https://castandcrew.atlassian.net/browse/H2G-6692) — Defaults, Deal Memo Maintenance | Need groom |
| [H2G-6694](https://castandcrew.atlassian.net/browse/H2G-6694) — Employee Onboarding Create (Valid/Draft) Status | Need groom |
| [H2G-6695](https://castandcrew.atlassian.net/browse/H2G-6695) — Deal Memo Onboarding Create (Valid/Draft) Status | Need groom |
| [H2G-6730](https://castandcrew.atlassian.net/browse/H2G-6730) — Document Validation, Employee + Corporation | Need groom |
| [H2G-6731](https://castandcrew.atlassian.net/browse/H2G-6731) — Document Validation, Deal Memo | Need groom |
| [H2G-6738](https://castandcrew.atlassian.net/browse/H2G-6738) — Mapping Values, Employee Maintenance | Need groom |
| [H2G-6739](https://castandcrew.atlassian.net/browse/H2G-6739) — Mapping Values, Deal Memo | Need groom |
| [H2G-6757](https://castandcrew.atlassian.net/browse/H2G-6757) — Onboarding Dashboard | Need groom |
| [H2G-6748](https://castandcrew.atlassian.net/browse/H2G-6748) — Onboarding Dashboard | Need groom |
