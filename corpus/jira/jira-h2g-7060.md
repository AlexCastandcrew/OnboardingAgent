# H2G-7060 — Excel import: Process File Tracking Dashboard Improvements

Source: Jira ticket H2G-7060 (type Story, status "Ready to Deploy", created 2026-09-28, updated 2026-10-26).

## Description

Improve the current Process File Tracking dashboard.

**Filters**
- Searching by any filter must show the latest status. For example, searching by Client currently returns only records with
  status QUEUED; it should display the latest records of the file.
- "Uploaded By" does not return records; it should search the partial value as displayed in the results.
- Add a Project column.

**Search results**
- When the file is processed through Studio+, show actual values (not IDs) for database and client.
- When the file has status Error, include the actual error as shown by Unstructured/AI.
- In the expanded results, make the Package Tracking ID a hyperlink to an AI Data page that shows the JSON (ideally including
  the actual JSON file), and make the file name a hyperlink to download the Excel file without logging in to CAPS or Studio+.

Related design in the corpus: `repo-excelmapping-design-7060`.
