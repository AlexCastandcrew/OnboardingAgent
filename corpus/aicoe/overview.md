# AICOE overview (partial)

Overview only, written on 2026-10-29. The AICOE code, its owners and its own documentation were not available for this
demo. Everything below is what the HTG documents say about AICOE from the HTG side. It is not a description of how
AICOE works inside.

## What it is, seen from HTG

AICOE (written "AiCOE" and "AI COE" in some tickets, and "UN" in the Executive Officer design) is the AI service that turns
uploaded files and documents into structured JSON. HTG does not extract that data itself for these flows: it reads
the JSON that AICOE publishes and maps it into HTG records.

## Where HTG uses it

1. **Excel Import (Unstructured files).** AICOE extracts an Excel worksheet into JSON and publishes it on the Kafka
   topic `aicoe-htg-unstructured-extract`. HTG's ExcelMapping reads that topic, applies the template and rule tables
   per worksheet, and sends the mapped timecards on to CAPS. The Executive Officer (EO) year-end payroll is the case the
   Excel Import epic H2G-6893 is adding: AICOE extracts the EO worksheet, and HTG turns that JSON into the timecard
   message for CAPS (H2G-7273).
2. **Onboarding Documents.** AICOE extracts onboarding documents, such as start packets uploaded in IPCP, into a package
   JSON. The package carries `dbCode`, `productionCompany`, `projectNumber` and `extractedAt` at the root, and an
   `employees` list where each employee has an identity and a list of forms (Start/Close, I-9, W-4, State W-4). Each form
   is a flat list of `{ field, value }` pairs. AICOE also echoes the `projectId` at the root of every onboarding package.
   HTG's ExternalDataMapper turns that package into the employee data that OnboardingImport consumes (H2G-6776).
3. **Data Extract screen.** HTG shows the AICOE extraction response of a processed worksheet. It reads the original
   message from Kafka, using the topic, partition and offset logged when the mapper received it, and no longer from
   Redis (H2G-7165, H2G-7266).

## What this overview does not cover

- The AICOE code, models and how the extraction works inside.
- Who owns AICOE and how to contact them.
- How to get access to AICOE or its environments.
- How to run or test AICOE locally.
- The full AICOE message contracts. The ones above are only what the HTG tickets quote.

Ask the team that owns AICOE for these. This assistant has no document that answers them yet.

## Sources used

HTG documents in this corpus: the Excel Import design for H2G-7273 (`repo-excelmapping-design-7273`), the AI Data design
for H2G-7165 (`repo-excelmapping-design-7165`), the tickets H2G-7273, H2G-7165 and H2G-6776, and the onboarding lookup
design (`repo-onboarding-lookup-fallback`).
