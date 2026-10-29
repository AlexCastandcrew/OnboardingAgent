# Onboarding W-4 Persistence — Design

Related: [Onboarding Import Service design](https://castandcrew.atlassian.net/wiki/spaces/HPS/pages/5422841860) (the contract and
the gap register this closes part of) and [Onboarding Mapping Service design](https://castandcrew.atlassian.net/wiki/spaces/HPS/pages/5558992937)
(the service that sends the W-4 data).

---

## Summary

**Why.** An onboarded employee's tax withholding forms, the federal W-4 and the state W-4, are read from the scanned documents
and mapped, and then lost: the service that saves the employee drops them. The employee arrives in HTG without their tax
withholding information, and someone has to type it in by hand.

**The design**
- **Saved like an I-9.** Each W-4 becomes a record of the employee, the same way the employee's I-9 statuses are saved: one
  federal W-4 record, and one state W-4 record per state.
- **The records HTG already has.** The federal and state W-4 record types already exist, so the employee screens and the search
  index show the new records with no other change.
- **Placed like the I-9 too.** A W-4 carries no project, so each record takes the region of the set's deal memo. Its production
  company and its state are stored as links to the HTG records, not as plain codes.
- **All or nothing.** The W-4 records are saved in the same transaction as the rest of the set. If one fails, nothing of the
  set is kept, and the message is retried.
- **One shared step.** The code that stores an employee's subrecords becomes one step shared by all subrecord types, so each
  W-4 type adds a line instead of another copy of the same loop.
- **Save what arrives.** The import does not check W-4 values and fills no defaults. Reading and checking the forms belongs to
  the mapping service.

**What it gives the business.** An employee onboarded from a package has their federal and state withholding on file from the
start, so nobody has to enter it by hand. The warning the import logs today for every W-4 stops.

**What we need from others.** Nothing. The mapping service already sends both W-4 lists with each employee.

**Risks we accept.** Two cases, both written in section 8:
- An employee who already exists and arrives in a later package gets additional W-4 records. Nothing is replaced or removed. The
  user sees the extra record on the employee and deletes it.
- The import resets the lock settings of an existing employee: the mapping service always sends the lock exemptions setting as "N"
  and the import writes it, so a locked employee is unlocked and still receives the
  new W-4 records. The user sees it on the employee, locks the employee again and deletes the extra records.

**Scope.** Saving the federal and state W-4 records of an onboarding set.

---

## 1. Problem

The mapping service sends `federalW4` and `stateW4` with each employee. The import has no place for them.

| Behavior | Consequence |
|---|---|
| The employee model of the import has no property for `federalW4` or `stateW4`, so both arrive in its bag of unmodeled fields. | That bag only holds single values: the generic field writer returns nothing for an array. Both lists are dropped. |
| For every key the document schema does not consume, the field writer logs "no matching schema entry ... dropped, not persisted". | Every employee with a W-4 produces one warning per list, and the W-4 data never reaches HTG. |

## 2. Goals and non-goals

**Goals**
- Every W-4 the mapping service sends is saved as a record of the employee.
- Saving them follows the pattern already used for I-9 statuses, so a reviewer has one pattern to know.
- The set stays atomic.

**Non-goals**
- Checking W-4 values or choosing defaults for missing ones.
- Replacing or merging records of an employee who already exists.
- Matching these records to anything Payroll sends later.
- Changing how the import writes the employee itself, including its lock settings.

## 3. Architecture

```
Kafka InOnboardingSaved  { packageId, onboardingSets[ employee { federalW4[], stateW4[] } ] }
   │
OnboardingEmployee ─────────── FederalW4s / StateW4s: the two lists, typed, bound to federalW4 / stateW4
   │
OnboardingMaterializer ─────── one transaction per set; commits or discards the set and its processed marker
   │
OnboardingEmployeeProvider ─── stores the employee, then each subrecord, inside the set's transaction
   │        ├── BuildFederalW4   item → subrecord employeeFederalW4
   │        ├── BuildStateW4     item → subrecord employeeStateW4
   │        └── ProjectRegionLookup   region from the project of the set's deal memo
   │
EmployeeSubRecord rows ─────── employeeFederalW4, employeeStateW4, linked by employee code and database code
   │
Employee export ────────────── after the commit: ToJson renders both types for the screens and the search index
```

| Part | Single job |
|---|---|
| `OnboardingEmployee` | Receive the two lists from the message, typed, so they are not lost in the bag of unmodeled fields. |
| `OnboardingMaterializer` | Commit or discard the employee, its subrecords and the processed marker together. |
| `OnboardingEmployeeProvider` | Turn each item into a linked employee subrecord and store it with the employee. |
| `ProjectRegionLookup` | Give the region of a project. |
| Employee export | Read the employee and its subrecords after the commit. |

## 4. Components

### 4.1 The contract in the import

- The wire names stay as the mapping service sends them, `federalW4` and `stateW4`. The model properties are plural, like the
  other subrecord lists, and are bound to the wire names explicitly. A name that did not bind would put the list back in the
  bag of unmodeled fields and lose it.
- Each item types only `productionCompany`, which the provider needs to build a link. Every other field stays a plain custom
  field, written against the registered schema of the record type. A new W-4 field needs a schema change, not a model change.
- A JSON `null` in any of the six subrecord lists (I-9 status, federal W-4, state W-4, direct deposit, tax waiver, permanent
  deduction) reads as an empty list, so the store step enumerates every list without a guard.
- Does not: validate any value.

### 4.2 The subrecords

- **Types.** `employeeFederalW4` and `employeeStateW4`, the existing types. Every field the mapping service sends exists in
  their schemas.
- **Link to the employee.** By employee code and database code, like every employee subrecord.
- **Region.** The W-4 has a production company but no project, so the region is read from the project of the set's deal memo,
  exactly as for an I-9 status.
- **Links to HTG records.** `productionCompany` and `state` are lookup fields. They are stored as the target record's
  ExternalId (`COMN|<code>`, because both types are shared across the region), never as the bare code the message carries. A
  bare code does not resolve and the field silently disappears from the employee.
- **`payrollCompany`.** It is also a lookup field in both schemas. The mapping service does not send it, so the builders do not
  convert it. If it starts to be sent, the builders must convert it to an ExternalId like `productionCompany`; as a bare code it
  would disappear from the employee.
- **Defaults.** None. A field the message does not send stays unset.
- **A W-4 that arrives without a state** is stored as received. Detecting it is the mapping service's job.

### 4.3 Saving

- The records are stored inside the set's transaction, after the employee and before the processed marker.
- The employee is queued for export after its subrecords are stored, and the export runs after the commit, so it sees them.

### 4.4 One store step for every subrecord type

- Every employee subrecord type goes through the same step: resolve the region of the item, build the linked record, store it.
  A type supplies only two things: where the region's production company and project come from and its builder. The types that
  take the region from the set's deal memo pass it as data, one scope shared by every item; the types whose items carry their
  own production company and project pass a function of the item.
- The four types saved today (I-9 status, direct deposit, tax waiver, permanent deduction) move onto the same step in the same
  change, so a new type adds one line, not another copy of the loop. Their behavior does not change. The only difference in
  order is that the two W-4 types are stored after the I-9 statuses and before the direct deposits.
- Trade-offs: the builder is passed as a delegate, because it is code and the types share no interface; the region scope is a
  delegate only for the three types whose items carry their own. The cost is that a reader follows a lambda per call instead of
  one plain loop. Plain loops cost one near-identical block per type, six after this change.

## 5. Failure modes

| Failure | What happens | Recovery |
|---|---|---|
| The project of the set's deal memo is not found | The region lookup throws. The set fails and nothing of it is kept. | The message is retried up to 5 attempts, then logged with the `[DLQ]` marker and dropped. Fix the project data and send the package again: the dropped message is only logged, not stored. |
| The database fails while saving a W-4 | The transaction rolls back the whole set. | Same retry. |
| The same message is delivered again | Sets already processed are skipped by their `referenceId`. No W-4 is duplicated. | None needed. |
| A pod stops in the middle of a set | The transaction is not committed. The message is redelivered. | None needed. |
| The package has no W-4 forms | The lists are empty and no record is created. | None needed. |
| A W-4 has a missing or bad field | It is stored as received. | Corrected on the employee, or upstream by the mapping service. |

## 6. Scale

N/A. A set adds at most one federal record and a few state records inside its existing transaction. Nothing changes in
throughput or ordering.

## 7. Observability

- No new log lines. The "no matching schema entry" warning stops for `federalW4` and `stateW4`.
- Existing logs keep carrying `referenceId`, `packageId` and the masked SSN, and every log of a message also carries the package
  identity (name, project, uploader) through the package log scope.
- W-4 values are never logged.
- After deploy: in Kibana, no "no matching schema entry" warning has `federalW4` or `stateW4` as the field. In HTG, an employee
  onboarded from a package with W-4 forms shows one federal record and one record per state.

## 8. Decisions and prerequisites

**Decisions**
- Every set adds new W-4 records, as it does for I-9 statuses. This choice has a cost, written below.
- The import neither applies nor keeps the lock settings of an employee: it writes the values the mapping service sends, which are always "N".

**Accepted limits**

| Limit | What happens | What it takes | Who notices | Recovery |
|---|---|---|---|---|
| Records accumulate for an existing employee | A second package for the same employee adds another federal record and more state records. None is replaced. The employee API already accepts several federal W-4 records and sorts them by effective date. | An employee who exists in HTG and is onboarded again. | The user, on the employee. | Delete the extra record. |
| The employee's lock settings are reset | An employee whose exemptions are locked (Y, F or S) is unlocked, and then receives the new records. The mapping service always sends the lock exemptions and lockout settings as "N", and the import writes the employee's fields as received. The lock is enforced when the employee is saved through the employee API, which the import does not use, and the lock depends on the stored employee, which only the import can see. This design does not add that rule. | A locked employee onboarded again. | The user, on the employee. | Lock the employee again and delete the extra records. |

**Prerequisites.** None.

## 9. Scenarios

| Scenario | Result |
|---|---|
| A package with one federal W-4 and state W-4 forms for CA and NY | The employee shows one federal record and two state records, each with its own state. |
| A package with no W-4 forms | No W-4 record is created. |
| The same message arrives twice | The second delivery skips the set. Nothing is duplicated. |
| The second state W-4 fails to save | The whole set is discarded, including the employee and the federal record. The message is retried. |
| The pod stops after the federal W-4 is stored but before the commit | Nothing is kept. The message is redelivered and processed from the start. |
| An existing employee is onboarded again | New W-4 records are added beside the old ones (section 8). |
| An employee with locked exemptions is onboarded again | The employee is unlocked and the new records are added (section 8). |
| Two packages for the same employee arrive in either order | Both add their records. The effective date orders them, not the arrival. |

Delete: N/A. The import never deletes.

## 10. Out of scope

- The `context` object of each set, which the import does not read.
- Matching these records to records Payroll later sends for the same employee.
- Checking W-4 values: the mapping service.
- Removing the "no matching schema entry" warning for fields other than the W-4 lists.

## References

N/A. The design uses no named pattern.
