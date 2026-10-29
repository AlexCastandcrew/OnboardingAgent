# H2G-7165 — AI Data: read AI extraction responses from Kafka instead of Redis

Source: Jira ticket H2G-7165 (type Story, status "Ready for UAT", created 2026-10-05, updated 2026-10-27).

## Description

The AI Data screen shows AI extraction responses from a temporary Redis cache. Responses disappear after a fixed period, so older
files cannot be reviewed, and the cache duplicates data that already exists on Kafka. The screen should show the extraction
response of a processed worksheet by retrieving the original message from Kafka, using the topic, partition and offset recorded
in the logs when the mapper received the message. The message is retrieved from the Unstructured extract topic the mapper reads.

The screen keeps letting an authorized user find and review processed Unstructured worksheets of a file, and AI extraction
responses are no longer stored in Redis. The list covers the last 14 days (the retention of the topic) and shows a file whose
extraction failed as a row of its own; paging beyond the newest 100 entries is H2G-6906. Traditional worksheets are covered by
H2G-7266; DocAI is retired and out of scope.

Related design in the corpus: `repo-excelmapping-design-7165`.
