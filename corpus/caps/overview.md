# CAPS overview (partial)

Overview only, written on 2026-10-29. The CAPS code, its owners and its own documentation were not available for this
demo. Everything below is what the HTG documents say about CAPS from the HTG side. It is not a description of how CAPS
works inside.

## What it is, seen from HTG

CAPS receives the timecards that HTG prepares and owns the processing that comes after. For Executive Officer (EO)
payroll, the Excel Import epic H2G-6893 states that CAPS keeps these responsibilities, and that HTG does not duplicate
them:

- tax calculation, including Symmetry / GTN processing;
- the existing EO tax override validations and the tax warning messages;
- batch and timecard processing.

CAPS also shows messages to the coordinator. For example, it warns when a tax override is below the calculated tax, or
when an override is entered but the calculated tax is zero.

## How HTG talks to CAPS

- **Timecards from Excel Import.** After ExcelMapping maps a file, it publishes a batch of timecards on the Kafka topic
  `htg-capspay-timecard-exported`. A `capspay-ingestion` pod resolves the employee, occupation, location, pay types and
  deductions in CAPS and publishes one message per timecard on `htg-capspay-timecard`. For EO files the message also carries
  a tax override amount per pay line and two timecard fields, `PaymentTerm` and `CAPS to Debit` (H2G-7273).
- **CAPS service in htg-server.** HTG calls CAPS through one service that carries clients, projects, occupations,
  employees and batches. It authenticates with a login token, adds `userId` and `source` to every body, and is registered
  with `AddCapsPay(configuration)` (External API design).
- **Names in the dashboards.** The Process File Tracking dashboard takes the client name and the project name from CAPS
  calls, one extra call per Studio+ file (H2G-7060).
- **Coordinators** use CAPS to correct what HTG could not decide. For example, an EO label that has no mapping rule row
  arrives as a payment, shows on the timecard, and the coordinator corrects it in CAPS (H2G-7273 design).

## What this overview does not cover

- The CAPS code, its database and how it calculates anything inside.
- Who owns CAPS and how to contact them.
- How to get access to CAPS or its environments.
- How to run or test CAPS locally.

Ask the team that owns CAPS for these. This assistant has no document that answers them yet.

## Sources used

HTG documents in this corpus: the Excel Import design for H2G-7273 (`repo-excelmapping-design-7273`), the dashboard design
for H2G-7060 (`repo-excelmapping-design-7060`), the External API design (`repo-external-api-design`), and the Jira epic
H2G-6893.
