# H2G-7273 — Excel import: Support Executive Officers JSON and pass to CAPS

Source: Jira ticket H2G-7273 (type Story, status "Ready for Code Review", created 2026-10-15, updated 2026-10-28).
The sample JSON attached to the ticket is not included here because it contains sample personal data.

## Description

Executive Officer (EO) year-end payments currently go through the legacy remote-desktop Excel import, which writes directly to
the database. EO files are moving to the AI pipeline so coordinators have timecards pre-loaded. The AI (UN) service extracts the
EO worksheet into JSON, and HTG must transform that JSON into the existing timecard-import message for CAPS. The requirements
live in the Confluence page "Executive Officers (EO) AI Automation".

The sample extraction (EO Allocation Worksheet) has worksheet-level fields such as pay period start and end dates, payroll
frequency, `PaymentTerm` (for example "CAPS to Debit") and `ZeroNetCheck`, and per-person timecards with estimated gross,
deductions, and distributions by work location with pay types such as GROSS WAGES and TAX OVERRIDE.

Related design in the corpus: `repo-excelmapping-design-7273`.
