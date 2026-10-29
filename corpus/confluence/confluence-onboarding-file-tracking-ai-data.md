# Onboarding Import - File Tracking and AI Data Visibility

Source: Confluence page 5637111813, space HPS, last modified oct 22, 2026.

# Overview

The Web UI has two screens that let users follow packages end to end and self-diagnose failures: **Process File Tracking** and **AI Data Query**. Today both screens only serve the Excel Mapping flow. This page describes what is needed so the Onboarding Import flow (AiCOE → ExternalDataMapper → OnboardingImport) shows up in the same two screens, using the same pattern as Excel Mapping.

**Fix version:** 18.7.0

**Target branch:** `majorfeature_2.0`

Scope decision: same pattern as Excel Mapping. Short data retention is acceptable. These screens exist so a user can see a failure shortly after it happens, not days later. No durable tracking table is planned.

# Current state

## Process File Tracking

- There is no SQL table. State is event-sourced from Serilog log lines in Elasticsearch.
- Writer: `FileTrackerProvider` (HTGPlus.ExcelMapping) logs lines with ProcessName `DashboardFileTracking` (file level) and `DashboardSheetTracking` (sheet level).
- Reader: `FileTrackerService` (HTGPlus.ExcelMapping.API) finds a `Queued` anchor event per file, then replays the later events on top of it. A file with no `Queued` event never appears.
- Endpoint: `POST ws/excelmapping/processfile/track`. UI: `HTGPlus.Web.UI/app/features/fileUpload`.
- The tracking id is the Studio+ `fileKey`. The Package Tracking ID is a field of the `InProgress` event.
- The status enum (Queued, InProgress, Routed, Extracted, Error, SentToCaps) is duplicated in two C# files and in the UI.

## AI Data Query

- Data lives in Redis under `ai_response_{worksheet}_{packageTrackingId}` with a 1 day TTL, written by `AIDataProvider` (HTGPlus.ExcelMapping).
- Endpoints: `GET ws/excelmapping/aiData`, `GET ws/excelmapping/aiData/{packageTrackingId}` and `GET ws/excelmapping/fileUrl/{fileKey}`. They are restricted to admins and marked `TODO: For QA Testing, To be deleted`.
- UI: `HTGPlus.Web.UI/app/features/aiData`. The detail view is hard-coded for timecard structures.

## Onboarding Import today

- No tracking events are emitted by ExternalDataMapper or OnboardingImport, so onboarding packages never appear in Process File Tracking.
- The AiCOE payload is not stored anywhere queryable, so there is nothing for AI Data Query to show.
- Failures, validation errors and dead-lettered messages exist only as log lines. The only persisted per-set state is `OnboardingProcessedReference`, and it records successes only.
- `fileKey` and `fileName` arrive in the AiCOE payload but `OnboardingPackageMapper` drops them, so downstream services cannot correlate to the original file.

# Target flow

1. **AiCOE** publishes an onboarding package (packageId, fileKey, fileName, dbCode, productionCompany, employees with referenceId) to `{env}-aicoe-htg-external-data-mapper`.
2. **ExternalDataMapper** receives it. It emits a file-level `Queued` tracking event, stores the raw AiCOE payload in Redis for AI Data Query, maps and verifies the package, then emits `InProgress` and publishes to `{env}-htg-onboarding-saved`. The message now carries `fileKey` and `fileName`. Validation failures and messages dropped after the maximum retries emit `Error`.
3. **OnboardingImport** consumes the message. For each `referenceId` it emits sheet-level events (Queued, InProgress, Imported or Error). When all sets of the package finish it emits the file-level result.
4. **ExcelMapping.API** exposes both record types, filterable by `PackageType`.
5. **Web UI** shows onboarding packages in Process File Tracking (with a per-employee expandable detail) and in AI Data Query (with an onboarding viewer).

# Tracking contract (proposal for onboarding)

| Tracking field | Excel Mapping | Onboarding |
| --- | --- | --- |
| TrackingId | Studio+ fileKey | AiCOE fileKey |
| PackageTrackingId | GUID from upload metadata | AiCOE packageId |
| PackageType (new) | ExcelMapping | Onboarding |
| Database | EOR name | dbCode |
| Client | CAPS client | productionCompany |
| FileName | Uploaded file name | fileName from AiCOE payload |
| UploadedBy / UploadDateTime | Studio+ upload metadata | Not available. extractedAt is used as the date, and UploadedBy is shown as AiCOE. |
| Sheet-level row | Worksheet | referenceId (one employee set) |

Proposed onboarding statuses: `Queued` (received by ExternalDataMapper), `InProgress` (mapped and verified, forwarded), `Imported` (new, persisted by OnboardingImport), `Error`. The status list must come from the API instead of being duplicated in the UI. The final names are to be confirmed during implementation of the first tracking tickets.

# Tickets

All tickets use fix version 18.7.0 and include acceptance criteria.

| # | Ticket | Why it is needed | Depends on |
| --- | --- | --- | --- |
| 1 | [H2G-7173](https://castandcrew.atlassian.net/browse/H2G-7173) | The provider is internal to ExcelMapping. ExternalDataMapper and OnboardingImport cannot reuse it until it is shared. This also removes the duplicated enum. | None |
| 2 | [H2G-7174](https://castandcrew.atlassian.net/browse/H2G-7174) | The mapper drops both fields, so OnboardingImport cannot correlate its events to the original file. | None |
| 3 | [H2G-7175](https://castandcrew.atlassian.net/browse/H2G-7175) | Creates the Queued anchor event that makes the package appear in Process File Tracking, plus InProgress and Error. | H2G-7173, H2G-7174 |
| 4 | [H2G-7176](https://castandcrew.atlassian.net/browse/H2G-7176) | AI Data Query reads from Redis. Nothing is stored for onboarding today. | H2G-7173 |
| 5 | [H2G-7177](https://castandcrew.atlassian.net/browse/H2G-7177) | Gives the user per-employee success or failure, which is the main thing they need to self-diagnose. | H2G-7173, H2G-7174 |
| 6 | [H2G-7178](https://castandcrew.atlassian.net/browse/H2G-7178) | Parity with H2G-7177 if OnboardingImportClean becomes the active implementation. **ON HOLD** until that decision is made. | H2G-7173, H2G-7174, H2G-7177 |
| 7 | [H2G-7179](https://castandcrew.atlassian.net/browse/H2G-7179) | The reader needs the PackageType filter, the new statuses and the onboarding column mapping. It also serves the valid statuses to the UI. | H2G-7173 |
| 8 | [H2G-7180](https://castandcrew.atlassian.net/browse/H2G-7180) | The endpoints must return onboarding entries with their type. The QA-only TODO markers are removed. | H2G-7176 |
| 9 | [H2G-7181](https://castandcrew.atlassian.net/browse/H2G-7181) | User-facing part of the tracking screen. | H2G-7179 |
| 10 | [H2G-7182](https://castandcrew.atlassian.net/browse/H2G-7182) | The current detail view only understands timecards. | H2G-7180 |
| 11 | [H2G-7183](https://castandcrew.atlassian.net/browse/H2G-7183) | Support needs to find onboarding events the same way they find Excel Mapping events (see EXCEL\_MAPPING\_LOGGING.md). | H2G-7175, H2G-7177 |

# Dependencies and order

- H2G-7173 is the first blocker. H2G-7175, H2G-7176, H2G-7177, H2G-7178 and H2G-7179 build on it.
- H2G-7174 blocks H2G-7175, H2G-7177 and H2G-7178.
- Backend (H2G-7175, H2G-7176, H2G-7177) and API (H2G-7179, H2G-7180) can run in parallel once H2G-7173 and H2G-7174 are done.
- UI tickets H2G-7181 and H2G-7182 follow the API tickets H2G-7179 and H2G-7180.
- External dependency: AiCOE must send a consistent `packageId` and `fileKey` in every onboarding payload.

# Out of scope and known limits

- No durable tracking table. Data follows the existing Elasticsearch log retention and the 1 day Redis TTL, same as Excel Mapping.
- The existing limits of the Excel Mapping reader (7 day default window, caps on filtered queries) are accepted as is.
- Access roles stay as they are today for both screens.
- Sending onboarding data to Payroll is covered by the H2G-5204 work and is not part of this effort.

# Open questions

- Is HTGPlus.OnboardingImportClean the active implementation? This decides whether H2G-7178 stays.
- Does AiCOE guarantee `packageId` and `fileKey` on every message, and is `packageId` always a GUID? The screen displays and sorts by it.
