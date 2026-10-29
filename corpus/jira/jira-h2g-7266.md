# H2G-7266 — Data Extract page: show Traditional extract data and rename AI Data to Data Extract

Source: Jira ticket H2G-7266 (type Task, status "Ready for UAT", created 2026-10-14, updated 2026-10-27).

## Description

The page that shows AI extraction results must show the extraction data of every processed worksheet, including worksheets of
Traditional files, and must be called "Data Extract" instead of "AI Data". The Process File Tracking dashboard links a file's
Package Tracking ID to this page for Traditional and Unstructured files. Traditional worksheets are read from the Traditional
extract topic in Kafka, using the topic, partition and offset logged by the mapper, with the same Kafka reader and worksheet
search delivered in H2G-7165. The retention of the Traditional extract topic is increased to 14 days on every environment.

Related design in the corpus: `repo-excelmapping-design-7266`.
