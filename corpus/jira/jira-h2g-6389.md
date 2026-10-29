# H2G-6389 — Onboarding Data Extract: Define JSON Contract and Build Import Service for Employees and Deal Memos

Source: Jira ticket H2G-6389 (type Story, status "Ready to Deploy", created 2026-07-17, updated 2026-09-25).
The reference mock JSON of the ticket is not included here because it contains sample personal and bank data.

## Background

The Data Extract team extracts data from onboarding documents (paper or digital). That data must flow into HTGPlus to create
Employees (with subrecords such as I-9, direct deposits, tax waivers and permanent deductions) and Deal Memos (with guarantees,
inclusions and allowances). Data Extract sends one Kafka message per onboarding document. The message holds an array of
employee plus deal memo sets, one per person.

Two levels of identity drive idempotency:

- `packageId`: unique per onboarding document / Kafka message, assigned by Data Extract.
- `referenceId`: unique per employee plus deal memo set, assigned by Data Extract.

Before processing a set, the service checks whether that `referenceId` was already processed and skips it if so, so
re-delivering the same message is safe. The new service uses an ORM approach like `HTGPlus.LookupEntityCore`, not raw stored
procedures, and consumes JSON (not Avro) from a new dedicated Kafka topic.

New hires: the employee is upserted by SSN (`dbCode|ssn`); the deal memo is always inserted as a new record and has no
number yet. Payroll assigns the number later and returns it through the existing Avro importer (`payroll-core-htg-dealmemo`,
`HTGPlus.ImportDealMemos`), which is out of scope. `dbCode` is stored as-is (for example `PAY`); the 2-digit year suffix is
added when HTG talks back to Payroll, also out of scope.

## Tasks

1. Define the JSON contract with Data Extract: `packageId` and per-set `referenceId`, stable across retries; ISO 8601 dates;
   employee referenced by SSN; no deal memo number; decide whether guarantees and allowances are nested or flat.
2. Build the Kafka consumer and import service: deserialize, loop through each set, skip processed `referenceId`s, create the
   employee first and then the deal memo, insert guarantees and allowances as subrecords, mark the `referenceId` processed,
   clear the Redis cache and queue Elasticsearch re-export. If one set fails, log it and continue with the others.
3. Error handling and retry: retry transient failures without losing the message; log permanent failures with `packageId`,
   `referenceId` and masked SSN. The SSN must always be masked in logs (last 4 digits only). The reference retry pattern of
   `HTGPlus.LookupEntityCore` forwards the external-topic message to an internal HTGPlus topic and re-queues it there on failure.

## Key files named in the ticket

`HTGPlus.LookupEntityCore` (ORM and retry reference), `HTGPlus.TimecardProcessor/Ingestion` (handler, parser, materializer,
transformer, service registration, topics), `HTGPlus.ImportEmployees`, `HTGPlus.ImportDealMemos`,
`HTGPlus.Business.Providers/Importer` and the HtgEmployee / HtgDealMemo Avro schemas.

## Out of scope

Deal memo number assignment, the `dbCode` year suffix, and a code-translation (mapping) layer for values Data Extract might
send in a format HTG does not understand.
