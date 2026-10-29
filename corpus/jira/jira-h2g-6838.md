# H2G-6838 — [ExternalDataMapper] Technical design doc + implementation diagram — Onboarding Mapping Service

Source: Jira ticket H2G-6838 (type Story, status "Deployed", created 2026-09-08, updated 2026-10-10). Related to H2G-6776.

## Goal

Produce a technical design document and an implementation/sequence diagram for the Onboarding Mapping piece of
`HTGPlus.ExternalDataMapper`, in the same format as the Onboarding Import Service design (H2G-6493).

## Scope

- Document the end-to-end flow: Kafka consumer (`OnboardingPackageHandler`, scaffolded in H2G-6624), mock ingestion, field
  mapping (per H2G-6776, H2G-6738 and H2G-6786) and publishing to the topic consumed by `OnboardingImport`.
- Diagram the mapping-relevant classes and their responsibilities (consumer, parser/serializer, mapper, publisher), following
  the Clean Code guidelines used for OnboardingImport (single responsibility, no god processors).
- Call out open questions, for example the target topic name and contract, and where the GUID per employee is generated.

## Deliverable and acceptance criteria

A Confluence page (HPS space) plus a diagram, linked back to H2G-6776. The design is reviewed, covers ingestion, mapping,
publishing and downstream OnboardingImport consumption, and is approved before implementation tickets start.
