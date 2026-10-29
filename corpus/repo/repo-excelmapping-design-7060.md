# Process File Tracking Dashboard — Filters, Names, Errors and Links — Design

Scope: what the Process File Tracking dashboard shows and how it filters, for files uploaded from HTG and from Studio+.

Term: the Data Extract page is the existing AI Data page. Separate work renames it; until then the package link opens that same
page under its current name.

---

## Summary

The Process File Tracking dashboard is where users follow an uploaded Excel file through processing. Each file is a row with its
database, client, uploader, status and error, and it expands into one row per worksheet.

**Why.** Users cannot answer basic questions from the dashboard. Files that came from Studio+ show numbers where the database,
client and project names should be. There is no project column. Searching by uploader only works when the whole value is typed
exactly. A failed AI extraction hides the stage where it failed. The file name and the package id are plain text, so a user who
wants the original Excel file or the extracted data must ask a developer or log in to another system.

**The design**
- **Names, not numbers.** For Studio+ files the client and project names are looked up when the file is received. The database name
  is turned from its number into its name when the dashboard loads.
- **A Project column** next to Client.
- **Partial search.** Typing part of an uploader, client or database finds it ("jane" finds `jane.doe@…`).
- **Fuller AI errors.** A failed AI extraction shows the stage where it failed and when, inside the existing error text.
- **File name opens the original file.** Any user who can see the dashboard can download it, without signing in to CAPS or Studio+.
- **Package id opens the extracted data.** For every file, whatever its execution type, administrators click the package id to see
  that package's worksheets in the Data Extract page. Everyone else sees plain text.

**What it gives the business.** Support and payroll staff read a file's database, client, project and failure reason from one
screen, and get to the source file and the extracted data in one click.

**What we need from others.** Nothing from other teams. The CapsPay ingestion tracking statuses must be deployed first, and the
Data Extract page must show extracted data for every execution type (separate work).

**Risks we accept.** Client and project names are not recovered for Studio+ files received before this change. If the name lookup
fails, the dashboard shows the number for that file. An uploader that appears only after the file is queued cannot be found by the
uploader search. The package link opens the Data Extract page even when no data is available for that package, and the page says
so. Every role that can open the dashboard can download any file, for any client, and the original file contains employee data.
Each, except the download, is written under Failure modes.

**Scope.** The dashboard screen, its API, and the tracking rows it reads. Not the processing of files, not how the Data Extract
page reads and shows its data.

---

## 1. Problem

| Behavior | Consequence |
|---|---|
| The Studio+ request builder fills database, project and client with only a number; the tracker falls back to that number. | Studio+ files show `1`, `8289`, not the database, project and client names. |
| The tracker writes a project value, but the API model and the dashboard table have no project. | No Project column. |
| The uploader filter is an exact match on the whole value, and the screen says so. | "jane" finds nothing. |
| A failed AI extraction tracks only its message; its stage and time are logged and dropped. | The user cannot tell where the AI failed. |
| File name and package id are plain text. | Opening the file or the extracted data needs a developer or another system. |

## 2. Goals and non-goals

Goals
- Database, client and project show names for every file processed from now on.
- Uploader, client and database are searched by part of their text.
- A failed AI extraction shows its stage and failure time.
- File name downloads the file; the package id opens the Data Extract page for that package, for administrators.

Non-goals
- Changing how files are processed, routed or posted.
- How the Data Extract page reads, stores and shows its data, and who may open it.
- Changing the file and sheet statuses, or how the dashboard derives them.

## 3. Architecture

```
Studio+ message ─► request builder ─► tracking rows (log entries in Elasticsearch)
HTG upload ──────►  (client, project names added)                │
                                                                  ▼
                                                           Tracker API ◄── Elasticsearch
                                                                  │  database name, project
                                                                  ▼
                                           Dashboard ──► file download endpoint
                                                     └─► Data Extract page (filtered to one package)
```

- **Request builder:** puts client and project names on the file's tracking rows.
- **Tracker API:** reads the rows, resolves the database name and applies the filters.
- **Dashboard:** shows the columns, filters and links.
- **File download endpoint:** returns the original file.
- **Data Extract page:** shows one package's worksheets and their extracted data.

## 4. Components

### 4.1 Request builder (Studio+ files)

- The client name comes from the CAPS client details call the builder already makes.
- The project name comes from the CAPS project list for the client, matched on the project id. This is one extra CAPS call per Studio+ file.
- If a lookup fails, the file keeps processing and the row carries the number. The failure is logged with the client and project ids.
- The database stays a number on the row; the Tracker API names it (4.2).
- Files uploaded from HTG already carry names and are unchanged.

Trade-offs: one extra CAPS call per Studio+ file; names are fixed when the file arrives and do not follow later renames.

### 4.2 Tracker API

- **Project.** The file model carries the project from the tracking row, and the dashboard shows it.
- **Database name.** After the rows of a page are combined, a database value that is a number is replaced by its name from the database lookup. An id not in the lookup stays a number. The mapping runs before filtering and sorting, so searching "CAPS" finds a file stored as `1`. Older Studio+ files are named too.
- **Filters.**

| Filter | Match | Where |
|---|---|---|
| Uploaded By | Case-insensitive contains | Elasticsearch, on the queued row, like File Name |
| Client | Case-insensitive contains on the displayed value | After the rows are combined |
| Database | Case-insensitive contains on the displayed name | After the rows are combined |
| Status, File Name | Unchanged | Unchanged |

Trade-offs: the uploader search only sees the uploader on the queued row.

### 4.3 AI error text

When the AI pipeline reports a failure, the error text written to the worksheet and file rows is `[<stage>] <message> (failed at <time>)`. The worksheet row shows it in its Error Message column. The file row keeps its summary ("Sheet 'X' has errors.").

### 4.4 Dashboard

- A Project column after Client.
- The Uploaded By placeholder no longer says "exact match".
- **File Name** is a link on every file. It downloads the original file from the existing download endpoint, using the row's tracking id as the file key. Every role that can open the dashboard is allowed by that endpoint, and opening the download to all of them is intended, so no further access check is added. If the file cannot be downloaded the user sees an error message.
- **Package Tracking ID** is a link on every file when the user is an HTG administrator or super administrator, whatever the execution type. In every other case it is plain text. The link opens the Data Extract page for that package.
- The expanded heading keeps the package id as plain text.

### 4.5 Data Extract page

- It accepts a package id in its address and lists only that package's worksheets, one row per worksheet.
- Who may open the page is unchanged.

## 5. Failure modes

| Failure | What happens | Recovery |
|---|---|---|
| Client or project lookup fails | The file keeps processing; the row shows the number; a warning names the ids | None; shown as a number for that file |
| Database id not in the lookup | The number is shown | Add the database to the lookup |
| Studio+ file received before this change | Database is named; client and project stay numbers | Accepted; ages out of the dashboard window |
| Uploader appears only on a later row | The uploader search does not find it | Accepted; search by file name or client |
| No extracted data is available for the package | The Data Extract page lists no entries for the package | None |
| File no longer in Studio+ | The download returns not found; the dashboard shows an error | None |
| Non-administrator opens the dashboard | File name link works; package id is plain text | None |

## 6. Scale

- The dashboard reads at most one page of files (up to 100). Nothing in this design adds a per-file call at load time.
- Searches that use client or database still combine rows for up to 2000 files in memory; this limit is unchanged.
- Ordering and concurrency are unchanged.

## 7. Observability

- The name-lookup warning uses the `[ExcelMapping]` prefix and carries the package tracking id, client id and project id.
- The uploader email was already shown on the dashboard. The AI error text can carry whatever the AI service returns and is already logged and shown. No new personal data is written to a log.
- Verify after deploy: upload a Studio+ file. Its row shows the database, client and project names. Search part of your user name in Uploaded By and find your files. As an administrator, click the package id of a Traditional file and of an Unstructured file and see only that package's worksheets. Click the file name and get the original file. Search Client with a value known to have several statuses and confirm each file shows its latest status, not Queued.

## 8. Decisions and prerequisites

- The CapsPay ingestion tracking statuses are deployed before this change.
- The Data Extract page shows extracted data for every execution type, including Traditional files, and is named Data Extract.
- No database change and no change from another team.

## 9. Out of scope

- How the Data Extract page reads its data (from Kafka instead of Redis), showing Traditional data, the rename from AI Data, who may open the page, and the authorization of its endpoints.
- The status rules and the limit of 2000 files on filtered searches.

## 10. References

N/A. No industry pattern is used.
