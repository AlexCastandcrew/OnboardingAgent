# Executive Officer timecard import — Design (typed tax override and two timecard fields)

Scope: an Executive Officer (EO) year-end worksheet extracted by the AI extraction service (UN) is mapped to the existing
timecard message for CAPS, with the tax override amounts carried as a typed field and two new fields, `PaymentTerm` and
`CAPS to Debit`, carried on every timecard message.

---

## Summary

Executive Officers are paid once a year with a lump sum that the client splits across states and cities, together with the
exact tax amount to withhold in each place (a "tax override"). Coordinators today load these files through a remote-desktop
tool that writes straight to the database, or key them by hand in December. This design routes EO files through the AI
import so the timecards are waiting for the coordinator when the client arrives.

**Why.** The AI import cannot carry an EO file to CAPS correctly. A tax override arrives as one more pay line, so CAPS
cannot tell it from wages. The payment terms are dropped on the way. A tax override of zero, which is a real instruction,
can be silently discarded.

**The design**
- **Tax override is its own field.** Each work-location line carries an optional tax override amount. Absent means the client
  gave none; zero means the client asked for zero.
- **Two timecard fields.** Every timecard message carries `PaymentTerm` and `CAPS to Debit`, read from the worksheet and
  passed unchanged, so CAPS can show the coordinator a message. Both are blank when the payroll is not an EO.
- **Rule tables do the labels.** Each EO payment or deduction label (401K, medical, shareholder payment and so on), the
  forced EO occupation and the work locations are mapped by the existing mapping rules, per worksheet. No EO branch in code.
- **Same flow.** The file follows the existing Unstructured path: batch creation, per-timecard messages, file tracking.
- **CAPS does the checking.** Name matching, the override-below-calculated-tax warning and the zero-override warning stay in
  CAPS.

**What it gives the business.** EO timecards arrive pre-loaded with their tax overrides, so December entry by hand and the
remote-desktop import stop.

**What we need from others.** The CAPS team accepts three additions to the timecard message it already receives (a tax
override amount on a line, `PaymentTerm` and `CAPS to Debit` on the timecard) and acts on them. UN sends the two fields at
worksheet level for EO files. The rule tables get rows for the EO worksheet (about 17 labels, the occupation, the
locations). An Unstructured template is assigned to the EO worksheet for each client.

**Risks we accept.** A label that has no rule row is turned into a payment, not a deduction, and shows on the timecard for
the coordinator to see. The coordinator corrects it in CAPS.

**Scope.** EO worksheets through the Unstructured path. The legacy tool stays until the business retires it.

---

## 1. Problem

| Behavior | Consequence |
|---|---|
| The EO tax override arrives as a pay line named `TAX OVERRIDE` and travels as a pay line to CAPS | CAPS cannot tell an override from earnings; without a pay-type rule it is flagged as an unmapped pay type. |
| A work-location line whose pay lines all have rate 0 and units at most 1 is removed before mapping | A line that only holds an explicit zero override is lost, and the client's zero instruction never reaches CAPS. |
| `PaymentTerm` and `CAPS to Debit` have no field in the Unstructured models or in the messages to CAPS | The coordinator is never told the payment term. |
| A deduction label with no deduction rule is turned into a Home State payment | An unmapped label such as 401K would be paid as wages. |
| When no template matches or mapping fails, the sheet falls back to raw timecards built outside the mapping flow | New fields must also exist on the raw path, or the override is lost there. |

## 2. Goals and non-goals

Goals
- Every EO value in the extraction reaches CAPS in a field CAPS can act on.
- A zero override and an absent override are different on the wire.
- No EO-specific branch in shared code.

Non-goals
- CAPS validations (override below the calculated tax, override with a zero calculated tax, the four EO occupation codes,
  employee name and SSN matching).
- The EO Excel template and the extraction itself (UN).
- The zero-net check: it is not one of the two new fields and is not carried.
- Retiring the remote-desktop tool.

## 3. Architecture

```
AICOE (UN) extraction JSON — aicoe-htg-unstructured-extract
        │
        ▼
UnstructuredMappingHandler ── template + rule tables (per worksheet) ──► ImportFileResult
   (raw fallback: no template / mapping error, same fields)                │
        │                                                                  │
        ▼                                                                  ▼
 file tracking                                   htg-capspay-timecard-exported (batch of timecards,
                                                 + tax override amount per line,
                                                 + PaymentTerm, CAPS to Debit per timecard)
                                                                           │
                                                                           ▼
                                                 capspay-ingestion pod (resolves employee, occupation,
                                                 location, pay type, deductions in CAPS)
                                                                           │
                                                                           ▼
                                                 htg-capspay-timecard (one message per timecard,
                                                 + tax override amount per line,
                                                 + PaymentTerm, CAPS to Debit)
```

- The handler turns the extraction into mapped timecards, as for every Unstructured file.
- The rule tables map labels, occupation and locations.
- The exported topic carries the mapped timecards, with the tax override and the two fields, to the ingestion pod.
- The ingestion pod resolves each timecard against CAPS and publishes the timecard message CAPS consumes.

## 4. Extraction models

- The extraction content carries `PaymentTerm` and `CAPS to Debit` as text next to the existing pay frequency; both models in
  the chain (the Unstructured model and the one the mapper reads) carry them, and the conversion between them copies them.
  A missing or blank value stays blank. The key `CAPS to Debit` is read and written exactly as spelled, spaces included.
- A distribution's pay lines named `TAX OVERRIDE` are lifted out of the pay lines into the distribution's tax override
  amount before any filtering. The name is the extraction contract with UN and is held once, as a constant.
- A blank or non-numeric amount gives no override; `0` and `0.00` give zero. UN sends the number.
- A distribution is dropped only when it has no pay line worth keeping and no tax override. A distribution that holds only an
  override of zero is kept.
- The same two fields and the override are carried by the raw fallback conversion.

Trade-offs: lifting by name ties the mapper to one label from UN. The alternative, asking UN for a dedicated field, costs a
second contract change and UN already ships this label.

## 5. Mapping

- Payment and deduction labels, the forced occupation (a rule keyed on the EO worksheet name, resolving to Executive Officer –
  No Coverage) and the work locations are rule-table rows, per worksheet. No code names a label.
- A label without a deduction row becomes a Home State payment, with its pay type taken from the pay-type rule when one
  exists. This is the shared behavior and is not changed.
- Pay frequency `Annual` converts to the CAPS tax frequency code `A`; blank gives no frequency.
- FEDERAL arrives as a state value. With no location rule it is forwarded as the line's state, with no error.
- Total gross wages is the timecard's estimated gross, as for every file.

## 6. From mapping to CAPS

Two hops carry the new fields, at timecard level. Each adds only the fields below.

| Hop | Carries | New field | Meaning |
|---|---|---|---|
| Exported topic (mapping → ingestion pod) | A batch of mapped timecards | `TaxOverrideAmount` on each work-location line | Null: none. Zero: client asked for zero. The tax level (federal, state, local) is the line's work location. |
| | | `PaymentTerm`, `CAPS to Debit` on each timecard | The worksheet's text, the same on every timecard of the sheet; blank when not an EO. |
| Timecard topic (ingestion pod → CAPS) | One message per timecard | `taxOverrideAmount` on each day item | Copied unchanged from the exported message. |
| | | `paymentTerm`, `CAPS to Debit` on the timecard | Copied unchanged from the exported message. |

The batch creation call to CAPS is unchanged.

The ingestion pod copies the amount and the two fields and adds no logic to them. A line that has a tax override and no pay line is kept and
published with its location and the override only; a line is dropped only when it has no pay line, no notes, no warning
and no override.

The end result is the message on `htg-capspay-timecard`, one per timecard, tied to the batch by `batchId`. For the first
timecard of an EO file (values in `<>` come from the rule tables and CAPS lookups; null fields are omitted):

```json
{
  "batchId": 12345,
  "firstName": "DEBORAH",
  "lastName": "JUNG",
  "ssn": "8963",
  "last4SSN": "8963",
  "estimatedGross": 10100,
  "taxFrequency": "A",
  "paymentTerm": "<text from the worksheet>",
  "CAPS to Debit": "<text from the worksheet>",
  "packageTrackingId": "<guid>",
  "externalReferenceId": "<guid>",
  "timecardDayItems": [
    {
      "workStartDate": "2025-01-01",
      "workEndDate": "2025-12-31",
      "locationState": "FEDERAL",
      "locationCountry": "US",
      "isForeignLocation": 0,
      "occupationCode": "<Executive Officer – No Coverage>",
      "taxOverrideAmount": 8000
    },
    {
      "workStartDate": "2025-01-01",
      "workEndDate": "2025-12-31",
      "projectLocationCode": "<CA>",
      "locationState": "CA",
      "locationCountry": "US",
      "isForeignLocation": 0,
      "occupationCode": "<Executive Officer – No Coverage>",
      "payType": "<mapped GROSS WAGES>",
      "payTypeUnits": 1,
      "rate": 1200,
      "taxOverrideAmount": 0
    },
    {
      "workStartDate": "2025-01-01",
      "workEndDate": "2025-12-31",
      "locationState": "CO",
      "locationCity": "Denver",
      "locationCountry": "US",
      "isForeignLocation": 0,
      "occupationCode": "<Executive Officer – No Coverage>",
      "payType": "<mapped GROSS WAGES>",
      "payTypeUnits": 1,
      "rate": 300
    }
  ],
  "timecardDeduction": [
    { "deductionTypeId": "<id>", "deductionType": "401K", "rate": 23500.00 },
    { "deductionTypeId": "<id>", "deductionType": "MEDICAL PRE-TAX", "rate": 1442.00 }
  ],
  "source": "<source>"
}
```

`taxOverrideAmount`, `paymentTerm` and `CAPS to Debit` are the only new fields in this message. A day item without
`taxOverrideAmount` has no override; one with `0` carries the client's zero. For a payroll that is not an EO, `paymentTerm`
and `CAPS to Debit` are blank.

CAPS shows the payment-term message to the coordinator and runs the tax override validations.

## 7. Failure modes

| Failure | What happens | Recovery |
|---|---|---|
| UN reports failure | File and sheets tracked as error with UN's message | Fix the file, re-upload |
| No template for the EO worksheet | Raw timecards, still carrying the override and the two fields; occupation and pay types unmapped, shown as errors in CAPS | Assign the template, re-upload |
| Mapping error on the sheet | Same raw fallback; sheet error logged | Fix rules, re-upload |
| Label without a rule row | Becomes a payment on the timecard | Add the rule row; correct in CAPS |
| Employee not found in CAPS | Error on that timecard in CAPS | Coordinator fixes |
| Publish to the exported topic fails | Timecard tracked as not sent | Re-upload |
| Ingestion pod cannot build a timecard | Timecard tracked as an error row with the reason; the others still publish | Fix the cause, re-upload |
| Publish to the timecard topic fails | Timecard tracked as not posted | Re-upload |

## 8. Scale

N/A. An EO file holds tens of timecards once a year; the flow and its batching are unchanged.

## 9. Observability

- Logs keep the existing scope: file key, package tracking id, worksheet, external reference id of the timecard.
- New log lines never carry names, SSNs or amounts of deductions; they carry the timecard's external reference id and the
  label that had no rule row.
- A mapped override logs "override applied" with the reference id and the level; a dropped distribution logs the reason.
- Verify in QA: upload the sample EO file; each message on the timecard topic shows `paymentTerm` and `CAPS to Debit`; a
  line with `0.00` carries `taxOverrideAmount` 0; the raw fallback carries the same fields.

## 10. Decisions and prerequisites

No open decisions. Prerequisites outside the code:
- The CAPS team accepts and acts on the three new fields.
- UN sends `PaymentTerm` and `CAPS to Debit` at worksheet level for EO files and omits them otherwise.
- Rule-table rows for the EO worksheet: labels, occupation, locations.
- An Unstructured template assigned to the EO worksheet for each client.
- UN applies the January 1 and December 31 defaults when the pay period is blank.

## 11. Out of scope

CAPS validations, the EO template, UN extraction, the remote-desktop tool.

## References

N/A. The design uses no named industry pattern.
