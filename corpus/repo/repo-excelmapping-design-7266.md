# Data Extract screen: Traditional worksheets — Design (source descriptors)

Scope: the screen that shows the extraction data of a processed worksheet is called "Data Extract" and shows the worksheets of
Traditional files as well as Unstructured ones. A Traditional worksheet is read from the Traditional extract topic in Kafka.

---

## Summary

The Data Extract screen lets an administrator review what the extraction produced for each worksheet of an uploaded payroll
file. It shows worksheets of Unstructured (AI) files only, and it is called "AI Data". The Process File Tracking dashboard
already links a file's Package Tracking ID (the identifier of one processing of an uploaded file) to this screen, but for a
Traditional file the screen has nothing to show.

**Why.** An administrator who follows the dashboard link for a Traditional file lands on an empty screen, and the name "AI Data"
is wrong for a screen that is meant to show every file.

**The design**
- **Same mechanism as Unstructured.** The Traditional mapper already logs, for every extract message it receives, the topic,
  partition and offset of that message with the file and worksheet. The screen finds a Traditional worksheet in those log
  entries and reads that one message from Kafka on demand. Nothing new is stored.
- **A list of sources.** The worksheet search and the Kafka reader work from a short list of sources. Each source names a
  mapper, its extract topic, where the worksheet name is in that mapper's log entry, and how its message becomes the screen's
  worksheet response. Unstructured and Traditional are the two entries.
- **Same screen, new name.** The list, the Package Tracking ID search and the worksheet detail work as they do for Unstructured
  worksheets. Every user-visible "AI Data" becomes "Data Extract".
- **Traditional detail.** The detail shows the mapped data the Traditional mapper received: timecards, their distributions and
  deductions, and the extraction errors.
- **Longer retention.** The Traditional extract topic keeps its messages for 14 days on every environment.

**What it gives the business.** The dashboard link works for Traditional files, and a Traditional worksheet can be reviewed for
14 days with no second copy of the data.

**What we need from others.** The Kafka team gives the API a read permission on the Traditional extract topic and sets its
retention to 14 days on every environment. Log retention is at least 14 days.

**Risks we accept.** A worksheet older than the topic or log retention disappears from the screen; the administrator sees
"message not available" and can review the file only by uploading it again. A worksheet whose extraction failed produces no
extract message and does not appear; its failure shows on the dashboard. The list depends on the names of the log fields; a
rename breaks the screen, which a test guards. The Traditional topic is the busiest extract topic, so 14 days of retention costs
more broker storage.

**Scope.** The list, search and detail of Traditional worksheets and the rename. Paging of the list is a separate design.

---

## 1. Problem

| Behavior | Consequence |
|---|---|
| The screen's search and reader accept one source: the Unstructured mapper's log entries and the Unstructured extract topic | A Traditional worksheet never appears. |
| The dashboard links every file's Package Tracking ID to the screen | For a Traditional file the link opens an empty list. |
| The menu entry and the page are labeled "AI Data" | The name is wrong once the screen shows Traditional worksheets. |

## 2. Goals and non-goals

Goals
- An administrator finds and opens any Traditional worksheet whose extract message is still on Kafka, from the list or by
  Package Tracking ID.
- Adding a further source later is one new entry in the list of sources, not new search or reader code.
- No Traditional extraction data is stored anywhere new.

Non-goals
- Changing what the mappers do with the message.
- Paging of the list and record counts.
- DocAI worksheets (retired).
- Renaming the API paths (section 4.5).

## 3. Architecture

```
Traditional extract topic ──► Traditional mapper ── logs "Message received" (Topic, Partition, Offset, ...) ──► Elasticsearch logs
Unstructured extract topic ─► Unstructured mapper ─ logs "Message received" (Topic, Partition, Offset, ...) ──►        ▲
                                                                                                                       │
Data Extract screen ──► API list ──► worksheet search (one query, all sources) ───────────────────────────────────────┘
        │
        └─► API detail ──► worksheet search (coordinates) ──► Kafka reader ──► extract topic
                                           └─► converter of that source ──► worksheet response ──► screen
```

- **Source list:** one entry per mapper that feeds the screen; the only place that knows the differences between mappers.
- **Mapper:** receives and logs the message; stores nothing for the screen.
- **Worksheet search (API):** turns "Message received" log entries of every source into worksheet rows with coordinates.
- **Kafka reader (API):** returns the message at one topic, partition and offset, for any source topic.
- **Converter (API):** one per source; turns that source's message into the worksheet response the screen renders.

## 4. Components

### 4.1 Source list

A source has: the mapper's process name, its extract topic, the log field that carries the worksheet names, and its converter.
Two sources exist:

| Source | Mapper process name | Extract topic | Worksheet names in the log entry |
|---|---|---|---|
| Unstructured | `UnstructuredMappingHandler` | Unstructured extract topic | `SheetNames` (a list; one message carries every worksheet of the file) |
| Traditional | `TraditionalMappingHandler` | Traditional extract topic | `WorksheetName` (one message is one worksheet) |

Topic names come from configuration. Data in a list, not a branch on the source in shared code.

Trade-offs: the differences between mappers live in one table, and the search reads each mapper's log fields as they are. Cost:
the table must be kept in step with each mapper's log fields; the contract test (4.2) pins them.

### 4.2 Mapper

- The Traditional mapper keeps its "Message received" entry and adds the file name to it (the list shows the file name; the
  Traditional extract message carries it, the entry does not). The scope already carries `Topic`, `Partition`, `Offset`,
  `FileKey`, `PackageTrackingId` and `WorksheetName`; the consumer that reads the topic puts the coordinates there.
- The mapper base class is shared with the DocAI mapper. The search filters on the process name, so DocAI entries do not appear.
- Contract the screen relies on, owned by the mappers and the shared consumer: the field names in the source list and the message
  text "Message received". A test pins them for every source.

### 4.3 Worksheet search

- One search over the "Message received" entries of every source in the list, in a date window (default 14 days) with paging.
  Each hit is read with its own source's worksheet field.
- A Package Tracking ID filter (exact match) lists only that package's worksheets. A package can hold Traditional and
  Unstructured worksheets; both are listed. A worksheet name belongs to one source in a package.
- One row per worksheet: package tracking id, file name, worksheet, file key, uploaded datetime (the time the mapper received
  the message), and its source. A redelivered message repeats its coordinates and is listed once.

### 4.4 Kafka reader and detail

- The reader accepts coordinates only from the worksheet search, never from the client, and only for a topic in the source list.
  Reading, expiry and unavailability behave as for Unstructured: manual assignment and seek, no group join, nothing committed;
  a message that is no longer on the topic is reported as expired, a missing one as unavailable.
- Detail input: package tracking id and worksheet. The latest matching entry gives the coordinates and the source.
- The Traditional converter reads the message and returns the mapped extraction of that worksheet: the file name, worksheet,
  file key, the worksheet response, success flag and, when the extraction carries errors, those errors. The response uses the
  property names the screen already reads for AI worksheets (`SSN`, `TimecardDistributions`, `Errors`) and adds
  `DeductionDistributions`, the Traditional counterpart of `OtherPaymentsAndDeductions`.
- Same role check as the screen (HTGPlusAdmin, SuperAdministrator).
- Personal data: the response holds SSN and tax data for the administrator who opened it; it is never logged.

### 4.5 Screen

- Menu entry, page heading and every other visible "AI Data" text become "Data Extract". The screen's route is `/extractdata`;
  the menu entry and the dashboard's Package Tracking ID link point to it. The API paths keep their names, so existing
  clients keep working.
- The list gets no new column. Timecard columns the Traditional response does not carry show as empty; the nested view renders
  `DeductionDistributions` like the other nested fields.
- Expired or unavailable shows "message not available".

## 5. Failure modes

| Failure | What happens | Recovery |
|---|---|---|
| Message older than topic retention | Detail returns expired; "message not available" | Upload the file again |
| Log entry older than log retention | Worksheet not in the list | Upload the file again |
| Kafka unreachable or timeout | Detail returns unavailable, logged with coordinates | Retry |
| Elasticsearch unreachable | List and detail fail with an error | Retry |
| Traditional worksheet failed to extract | No extract message, no row | The failure is on the dashboard |
| Mapper redelivers a message | Same coordinates, one row | None needed |
| A mapper's log field is renamed | The contract test fails the build | Fix the table or the mapper |
| Topic missing from the source list | The reader refuses it, logged | Add the source |

## 6. Scale

Admin-only screen, low volume. A detail open is one search and one single-message read. The list is one paged search; Traditional
adds many more received entries than Unstructured, bounded by the date window and the page size.

## 7. Observability

- API logs list and detail with package tracking id, worksheet, source, topic, partition, offset and result (found, expired,
  unavailable), prefixed `[AIData]` as the Unstructured path does.
- Verify after deploy: process a Traditional file, open its Package Tracking ID from the dashboard, open each worksheet.

## 8. Decisions and prerequisites

No open decisions. Prerequisites outside HTG code: read permission for the API on the Traditional extract topic; retention of
that topic at 14 days on every environment and log retention of at least 14 days. The worksheet search and the Kafka reader
for the Unstructured source exist before this design is built.

## 9. Out of scope

Paging and record counts of the list. File download. Renaming the API paths.

## References

- Claim Check pattern (the log entry is the key, the Kafka message is the stored data):
  https://www.enterpriseintegrationpatterns.com/patterns/messaging/StoreInLibrary.html
- Kafka consumer manual partition assignment and `seek`:
  https://kafka.apache.org/41/javadoc/org/apache/kafka/clients/consumer/KafkaConsumer.html
