# H2G-7382 — Extract Traditional timecard data inside htg-server instead of calling the DataIngestion service

Source: Jira ticket H2G-7382 (type Story, status "Ready for UAT", created 2026-10-21, updated 2026-10-27).

## Description

The Traditional extraction code has been migrated into htg-server under H2G-6864. It now needs to be used for Traditional-template
files, and the external call to the DataIngestion service retired. Traditional-template files must be extracted inside htg-server
and produce the same timecards as today, so the DataIngestion service can be retired.

Related design in the corpus: `repo-excelmapping-design-7382`.
