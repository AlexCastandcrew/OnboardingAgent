# H2G-6493 — Design: Onboarding Import Service Architecture and Data Flow

Source: Jira ticket H2G-6493 (type Architecture, status "Design Accepted", created 2026-07-28, updated 2026-09-25).

## Goal

Produce the technical design document for the Onboarding Import Service before implementation begins (H2G-6424). The design
must cover architecture, data flow, entity mapping, idempotency strategy and error handling.

## Kafka message contract

The full Kafka message contract is documented in the Confluence page "Onboarding Kafka Message Contract - Employee + DealMemo".
The design must be consistent with it. Key contract points the design must address:

- Top-level fields: `packageId`, `onboardingSets[]`, fileKeys, clientId, projectId, fileName.
- `referenceId` is the idempotency key. It is assigned by Data Extract and is stable across Kafka retries.
- The SSN is sent without dashes (9 digits). It must never appear in plain text in any log; mask it to the last 4 digits.
- The deal memo has no number in the payload; Payroll assigns it later.
- The employee is referenced in the deal memo by SSN (field `employee`), not by internal GUID.
- Codes (unions, pay codes, states) are plain strings, with no `COMN|` or `DBCODE|` prefix.
- Field names differ from the HTGPlus REST API (for example `wciCode` not `wcompCode`, `pensionUnion` not `pensionUnionId`).
- A failure on one set must never block the remaining sets in the same message.

## Deliverables

- Architecture diagram of the Kafka consumer pipeline (Consumer, Processor, Active Record).
- Data flow diagram from the Kafka message to the persisted Employee and Deal Memo.
- Entity mapping table: onboarding JSON fields to HTGPlus database fields.
- Idempotency strategy: `referenceId` deduplication.
- Error handling: retry topics, dead-letter queue and failure logging with SSN masking.
- Project structure: new service assembly, folder layout and dependencies.
