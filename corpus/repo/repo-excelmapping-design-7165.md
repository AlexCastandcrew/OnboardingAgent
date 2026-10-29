# AI Data from Kafka — Design (log-resolved read)

Scope: the AI Data admin screen reads the extraction response of a processed worksheet from the Kafka extract topics instead of
Redis. Redis no longer holds AI extraction responses.

---

## Summary

The AI Data screen lets an administrator review what the extraction produced for each worksheet of an uploaded payroll file.
Today it reads from a temporary Redis cache that keeps each response for one day, and that cache duplicates data that already
sits on Kafka.

**Why.** After one day an administrator can no longer review a worksheet, and we pay to store a second copy of every response.

**The design**
- **Kafka is the only store.** The mappers stop writing the response to Redis. The message they already consume from the extract
  topic is the record.
- **The log is the address book.** Each mapper already logs, for every message it receives, the topic, partition and offset of
  that message together with the file and worksheet names. The screen finds worksheets by searching those log entries.
- **Fetch on demand.** When an administrator opens a worksheet, the API reads that one message from Kafka by its topic, partition
  and offset.
- **Same screen, same data.** The list columns and the worksheet detail are what the screen shows today. The API shows
  the worksheet content as the extraction sent it, read through the message model that the mapper and the API share in the
  ExcelMapping core project. The list also shows every worksheet of a received message, including worksheets with no content, and files whose extraction
  failed, which the Redis copy never held.
- **Search by package.** The screen gets a Package Tracking ID field (the identifier of one processing of an uploaded file);
  the list then shows only that package's worksheets.
- **Same audience.** Only the administrator roles that can open the screen today can read a response.

**What it gives the business.** A worksheet can be reviewed for as long as its Kafka message and its log entry exist (14 days,
against one day today), with no second copy of the data.

**What we need from others.** The Kafka team gives the API a read permission on the Unstructured extract topic and sets the
retention of that topic to 14 days on every environment (7 days on QA2 today). The log retention is at least 14 days.

**Risks we accept.** A worksheet older than the topic retention or the log retention disappears from the screen. The
administrator sees "message not available" and can review the file only by uploading it again. The list depends on the names of
the log fields; a rename breaks the screen, which a test guards. The "File Uploaded Datetime" column shows the time the mapper
received the message.

**Scope.** The AI Data list and detail for Unstructured worksheets, as they are shown today, plus a Package Tracking ID search.
DocAI worksheets leave the screen.
Traditional worksheets are not shown; that belongs to the screen's own design.

---

## 1. Problem

| Behavior | Consequence |
|---|---|
| The response is written to Redis with a one-day lifetime (`ai_response_{worksheet}_{packageTrackingId}`) | A worksheet cannot be reviewed after one day. |
| The list is built by scanning every Redis key with the prefix and fetching all values | Cost grows with every stored response; each value holds a full worksheet. |
| The same data is already in the extract topic message | Two copies of every response. |

## 2. Goals and non-goals

Goals
- An administrator can find and open any worksheet whose extract message is still on Kafka, with the same columns and the same
  detail as today.
- An administrator can list the worksheets of one package by its Package Tracking ID.
- No Unstructured extraction response is stored in Redis.
- No new write path: nothing new is stored anywhere.

Non-goals
- Changing what the mappers do with the message.
- Changing topic retention (a prerequisite, section 8).

## 3. Architecture

```
Unstructured extract topic
        │ consumed by mapper
        ▼
 mapper logs "Message received" with Topic, Partition, Offset, FileKey, PackageTrackingId, worksheet names ──► Elasticsearch logs
                                                                                                               ▲
 AI Data screen ──► API list ───────────────────────────── search received entries ───────────────────────────┘
        │
        └─► API detail ── resolve coordinates (same search) ──► KafkaMessageReader (assign, seek, read one) ──► extract topic
                                                         └─► worksheet content from the shared message model ──► screen
```

- **Mapper:** receives and logs the message; stores nothing for the screen.
- **Worksheet search (API):** turns received-message log entries into worksheet rows with coordinates.
- **Kafka reader (API):** returns the message at one topic, partition and offset.
- **Detail (API):** takes the requested worksheet's content from that message, using the message model shared with the mapper.

## 4. Components

### 4.1 Mapper

- Removes the Redis write. The received-message log entry is the index; it is kept as it is.
- Contract the screen relies on, owned by the mappers and the shared consumer: the log scope fields `Topic`, `Partition`,
  `Offset`, `PackageTrackingId`, `FileKey`, `ProcessName`, `FileName`, the `SheetNames` property of the "Message received"
  entry, and that message text. A test pins these names and the text.
- Trade-off: the screen depends on log field names instead of a typed contract. Gain: nothing new is written and the shared
  consumer is not changed.

### 4.2 Worksheet search

- Searches the entries written by the Unstructured mapping handler (`ProcessName`) whose message text is "Message received", in
  a date window (default 14 days) with paging. The text filter excludes the handler's other entries, which carry the same
  coordinates but no worksheet names. A Package Tracking ID filter (exact match) lists only that package's worksheets; file name and file key
  filters are optional.
- One row per worksheet, with the columns the screen shows: package tracking id, file name, worksheet, file key, uploaded
  datetime. An Unstructured message carries the file's worksheets, one row each, all with the same coordinates.
- A failure message can carry no worksheet names; it appears as one row for the file with an empty worksheet.
- A redelivered message produces entries with identical coordinates; rows are de-duplicated on topic, partition, offset and
  worksheet.
- The received time fills the uploaded datetime column.

### 4.3 Kafka reader

- Assigns one partition manually, seeks to the offset, reads one message with a timeout, commits nothing, and closes. It does
  not join a consumer group (manual assignment; see References); the group id in its configuration only satisfies the client.
- It accepts coordinates only from the worksheet search, never from the client, and only for the Unstructured extract topic.
- The reader sets `AutoOffsetReset` to `Earliest`. When the requested offset is below the topic's start, the client then moves
  to the first retained message and returns it; the returned message's partition and offset must equal the requested ones, and
  a mismatch is reported as expired.
- No message within the timeout is reported as unavailable. Both are expected results, returned as a result value, not thrown.

### 4.4 Detail

- Input: package tracking id and worksheet. The latest matching entry gives the coordinates.
- Output: the same fields the screen reads today: file name, worksheet, package tracking id, file key, the worksheet response, a
  success flag, and the error message when the extraction failed (the pipeline error message of the file response).
- The worksheet response is the requested worksheet's content from the message, as the extraction sent it, wrapped as
  `{ Content: ... }`. The message model (the AICOE extraction response) and the log contract of section 4.1 live in the
  ExcelMapping core project, `HTGPlus.ExcelMapping.Core`, which the mapper and the API both reference. The mapper converts the
  same model to the response it hands to template mapping.
- Trade-off: the screen shows the content as sent, not as converted for template mapping. A missing reference id stays empty
  instead of getting a generated id, and a pay type rate stays the text the extraction sent. Gain: the API depends on neither
  the mapper host nor the external-client project.
- Same role check as the screen (HTGPlusAdmin, SuperAdministrator).

### 4.5 Screen

- The list gets a Package Tracking ID field. An empty field shows the date-window list; a value shows only that package's
  worksheets. The columns, the double-click to open and the detail view do not change.
- An expired or unavailable result shows "message not available" instead of an error.

## 5. Failure modes

| Failure | What happens | Recovery |
|---|---|---|
| Message older than topic retention | Detail returns expired; the screen says "message not available" | Upload the file again |
| Log entry older than log retention | Worksheet does not appear in the list | Upload the file again |
| Kafka unreachable or timeout | Detail returns unavailable, logged with coordinates | Retry |
| Elasticsearch unreachable | List and detail fail with an error | Retry |
| Several entries for one worksheet (file processed twice) | Each processing has its own package tracking id and its own row | None needed |
| Worksheet name not in an Unstructured message | Detail returns not found | None |

## 6. Scale

Admin-only screen, low volume. A detail open is one search and one single-message read. The list is one paged search; its cost
does not grow with stored responses.

## 7. Observability

- API logs list and detail with package tracking id, worksheet, topic, partition, offset and the result (found, expired,
  unavailable), prefixed `[AIData]`.
- The payload holds employee data; it is never logged.
- Verify after deploy: process an Unstructured file, open its worksheets, and confirm no `ai_response_` keys are created in
  Redis for it.

## 8. Decisions and prerequisites

No open decisions. Prerequisites outside HTG code: read permission for the API on the Unstructured extract topic; retention of
that topic set to 14 days on every environment (7 days on QA2 today) and a log retention of at least 14 days; a dedicated
consumer group id for the reader.

## 9. Out of scope

DocAI and the legacy pipeline are retired: their worksheets are not shown and their code is removed separately, so this design
does not change them; file download. Traditional worksheets on the screen and the screen rename are a separate design that
builds on this design's Kafka reader and worksheet search.

## References

- Claim Check pattern (the log entry is the key, the Kafka message is the stored data):
  https://www.enterpriseintegrationpatterns.com/patterns/messaging/StoreInLibrary.html
- Kafka consumer manual partition assignment and `seek`:
  https://kafka.apache.org/41/javadoc/org/apache/kafka/clients/consumer/KafkaConsumer.html
