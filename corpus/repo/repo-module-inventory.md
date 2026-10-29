# H2G Module Inventory

### Current state of `HTGPlus.Web.UI/app/` — updated per diagnostic run

> **Status (2026-10): historical.** This inventory describes `app/` before the HTG-6974 TypeScript migration.
> `app/` now holds only `core/`, `routes/`, `features/` and `shared/`; see the
> [Web.UI developer guide](../HTGPlus.Web.UI/README.md).
>
> This is a living document. Update it after each diagnostic run or significant structural change.
> For the target structure every module should migrate toward, see [ARCHITECTURE_MIGRATION.md](ARCHITECTURE_MIGRATION.md).

---

## Current Folder Tree

The tree below reflects the actual state of `HTGPlus.Web.UI/app/` (3 levels deep; folders with more than 6 files are summarized).

```
HTGPlus.Web.UI/app/
    +-- accounts/               # Login / auth screens (2 files)
    +-- accrualPayout/          # Accrual payout feature — has own store, no services folder
    |   +-- notification/
    |   +-- store/
    |   +-- [6 files]
    +-- adjustments/            # Adjustments feature — has own store, no services folder
    |   +-- store/
    |   +-- [9 files]
    +-- aiData/                 # AI data feature — components + hooks, no store
    |   +-- components/
    |   +-- hooks/
    +-- api/                    # ⚠ Centralized API layer — all modules share one folder
    |   +-- accrualPayout/      #   API calls are NOT co-located with their module
    |   +-- batch/
    |   +-- bulkEdit/
    |   +-- client/
    |   +-- corporation/
    |   +-- dashboard/
    |   +-- dealmemo/
    |   +-- employees/
    |   +-- invoice/
    |   +-- newAdmin/           # ⚠ new* prefix — unfinished module
    |   +-- newBatches/         # ⚠ new* prefix — unfinished module
    |   +-- newInvoices/        # ⚠ new* prefix — unfinished module
    |   +-- newRuleVersions/    # ⚠ new* prefix — unfinished module
    |   +-- newSetup/           # ⚠ new* prefix — unfinished module
    |   +-- prodco/
    |   +-- producer/
    |   +-- Project/
    |   +-- rtce/
    |   +-- rules/
    |   +-- timecardRules/
    |   +-- [6 more folders]
    +-- assets/                 # Static assets (images, fonts)
    +-- batch/                  # Batch module — has store; overlaps with batchesUtilities
    |   +-- store/
    +-- batchesUtilities/       # ⚠ Batch UI components — unclear boundary with batch/
    |   +-- [16 files]
    +-- bulkEdit/               # Bulk edit — uses Redux (reducer/sagas/actions) — legacy pattern
    |   +-- bulkEditTable/
    |   +-- common/
    |   +-- header/
    |   +-- modal/
    |   +-- notification/
    |   +-- shared/
    |   +-- sideBar/
    |   +-- utils/
    |   +-- [9 files — Redux artifacts: reducer, sagas, actions, constants]
    +-- client/                 # Client module — mixes components + store + service + mixins
    |   +-- components/
    |   +-- CustomMixins/
    |   +-- DeprecatedMixins/
    |   +-- store/
    +-- common/                 # ⚠ Catch-all shared components — 50+ files, no clear boundary
    |   +-- components/
    |   +-- Fields/
    |   +-- layouts/
    |   +-- mixins/
    |   +-- PayrollAgreement/
    |   +-- Table/
    |   +-- UnionAgreements/
    |   +-- Utils/
    |   +-- [30+ files at root — mixed generic + feature-specific]
    +-- corporation/            # Corporation module — mixes components + store + service + mixins
    |   +-- components/
    |   +-- CustomMixins/
    |   +-- DeprecatedMixins/
    |   +-- store/
    +-- dealmemo/               # Deal memo module — has store, helper.js is 84,008 bytes
    |   +-- accordions/
    |   +-- DealMemoLeftSection/
    |   +-- DealMemoSync/
    |   +-- store/              # ⚠ dealMemoStore.js — 3,596 lines — god store
    |   +-- topSection/
    +-- documents/              # Documents module — has store
    |   +-- store/
    +-- docuSign/               # DocuSign module — has store, hooks, components
    |   +-- components/
    |   +-- hooks/
    |   +-- store/
    +-- employees/              # Employees module — has store; helper.js is 79,614 bytes
    |   +-- accordions/
    |   +-- store/              # ⚠ employeeStore.js — 4,036 lines — god store
    +-- fileUpload/             # File upload module — has store
    |   +-- store/
    +-- newAdmin/               # ⚠ Unfinished module — new* prefix, has store, no clear owner
    |   +-- permissions/
    |   +-- scheduler/
    |   +-- store/
    +-- newBatches/             # ⚠ Unfinished module — single file only, new* prefix
    +-- newDashboard/           # ⚠ Unfinished module — new* prefix; has store
    |   +-- dashboardTable/
    |   +-- leftDashboard/
    |   +-- projectDashboard/
    |   +-- projectRequestStatus/
    |   +-- store/              # ⚠ dashboardStore.js — 1,470 lines — approaching god store
    +-- newInvoices/            # ⚠ Unfinished module — new* prefix; store lives inside module
    |   +-- InvoicesStore.js    # ⚠ 1,979 lines — god store, not in a store/ subfolder
    +-- newRules/               # ⚠ Unfinished module — new* prefix; store lives as flat file
    |   +-- RuleTableStore.js   # ⚠ 54,316 bytes — large store, not in a store/ subfolder
    +-- newRuleVersions/        # ⚠ Unfinished module — new* prefix; has store/
    |   +-- store/
    +-- newSetup/               # ⚠ Unfinished module — new* prefix; has store/
    |   +-- store/
    +-- newViews/               # ⚠ Unfinished module — new* prefix; store.js at root level
    +-- popoutScreens/          # Popout screens — AuditLog + comments sub-folders
    |   +-- AuditLog/
    |   +-- comments/
    +-- prodco/                 # Production Company module — mixes components + service + mixins
    |   +-- components/
    |   +-- CustomMixins/
    |   +-- hooks/
    |   +-- Mixins/
    |   +-- store/
    |   +-- topSection/
    +-- producer/               # Producer module — mixes components + service + mixins
    |   +-- components/
    |   +-- CustomMixins/
    |   +-- DeprecatedMixins/
    |   +-- store/
    +-- profile/                # User profile — single file only
    +-- project/                # Project module — has store, components
    |   +-- components/
    |   +-- leftPanel/
    |   +-- mainPanel/
    |   +-- store/              # ⚠ ProjectStore.js — 2,727 lines — god store
    |   +-- topPanel/
    +-- services/               # App-level services — ajax, auth, session (correctly placed)
    +-- shared/                 # Partial shared layer — popups + one Constants.js
    |   +-- approverDialogPopup/
    |   +-- confirmDialogPopup/
    |   +-- docusignDialogPopup/
    |   +-- projectDashboard/
    +-- store/                  # Root store index — only 2 files (CacheStore.js, index.js)
    +-- styles/                 # Global CSS/SASS styles
    +-- timecard/               # Timecard module — largest module; has store, utils, components
    |   +-- breakdown/
    |   +-- components/
    |   +-- paidHours/
    |   +-- store/              # ⚠ batchHeaderStore.js — 1,802 lines — god store
    |   |                       # ⚠ rtceStore.js — 3,418 lines — god store
    |   +-- styles/
    |   +-- timecardDetailsDialog/
    |   +-- timecardTable/
    |   +-- utils/
    +-- timecardRules/          # Timecard rules module — has store, editor
    |   +-- editor/
    |   +-- store/
    +-- utility/                # App-level utilities
    +-- app.js                  # App entry — belongs here
    +-- Application.js          # ⚠ Root component at app/ root — should be in routing/
    +-- AppHistory.js           # ⚠ Router history — should be in routing/
    +-- CrossScreenModal.js     # ⚠ Global modal at app/ root — should be in shared/
    +-- MainPage.js             # ⚠ Main layout at app/ root — should be in routing/ or shared/
    +-- reducer.js              # ⚠ Redux root reducer at app/ root — legacy artifact
    +-- RootStore.js            # ⚠ 15,446 bytes — root store at app/ root, too large
    +-- sagas.js                # ⚠ Redux sagas at app/ root — legacy artifact
```

---

## Known Problems

| Problem                               | Affected areas                                                                                                                                                                     | Migration action                                                       |
| ------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| **8 unfinished `new*` folders**       | `newAdmin`, `newBatches`, `newDashboard`, `newInvoices`, `newRules`, `newRuleVersions`, `newSetup`, `newViews`                                                                     | Rename + restructure to full Shield module before adding any new code  |
| **7 god stores**                      | `employeeStore` (4,036 lines), `dealMemoStore` (3,596), `rtceStore` (3,418), `ProjectStore` (2,727), `InvoicesStore` (1,979), `batchHeaderStore` (1,802), `dashboardStore` (1,470) | Split into per-domain stores inside their module                       |
| **Centralized `api/` folder**         | All modules                                                                                                                                                                        | Move API calls into `modules/[name]/services/` as modules migrate      |
| **`common/` catch-all**               | 50+ files, mixed generic + feature-specific                                                                                                                                        | Generic UI → `shared/components/`; feature-specific → their module     |
| **Mixed responsibilities in modules** | `prodco`, `client`, `corporation`, `producer`                                                                                                                                      | Extract service + mixins into `services/` subfolder per Shield pattern |
| **3 overlapping batch folders**       | `batch/`, `batchesUtilities/`, `newBatches/`                                                                                                                                       | Merge into single `batches/` Shield module                             |
| **`bulkEdit/` uses legacy Redux**     | `reducer.js`, `sagas.js`, `bulkEditActions.js`                                                                                                                                     | Replace with MobX store when module is touched                         |
| **App-level files at `app/` root**    | `Application.js`, `AppHistory.js`, `MainPage.js`, `CrossScreenModal.js`, `RootStore.js`                                                                                            | Move to `routing/` or `shared/` during migration                       |

---

## Module Migration Status

Update this table as modules are migrated to the Shield pattern.

| Module           | Has `index.js` | Store split | Services co-located | Lazy loaded | Status      |
| ---------------- | -------------- | ----------- | ------------------- | ----------- | ----------- |
| `employees/`     | ☐              | ☐           | ☐                   | ☐           | Not started |
| `dealmemo/`      | ☐              | ☐           | ☐                   | ☐           | Not started |
| `timecard/`      | ☐              | ☐           | ☐                   | ☐           | Not started |
| `project/`       | ☐              | ☐           | ☐                   | ☐           | Not started |
| `prodco/`        | ☐              | ☐           | ☐                   | ☐           | Not started |
| `client/`        | ☐              | ☐           | ☐                   | ☐           | Not started |
| `corporation/`   | ☐              | ☐           | ☐                   | ☐           | Not started |
| `producer/`      | ☐              | ☐           | ☐                   | ☐           | Not started |
| `newInvoices/`   | ☐              | ☐           | ☐                   | ☐           | Not started |
| `newDashboard/`  | ☐              | ☐           | ☐                   | ☐           | Not started |
| `newRules/`      | ☐              | ☐           | ☐                   | ☐           | Not started |
| `bulkEdit/`      | ☐              | ☐           | ☐                   | ☐           | Not started |
| `adjustments/`   | ☐              | ☐           | ☐                   | ☐           | Not started |
| `accrualPayout/` | ☐              | ☐           | ☐                   | ☐           | Not started |
| `docuSign/`      | ☐              | ☐           | ☐                   | ☐           | Not started |
| `documents/`     | ☐              | ☐           | ☐                   | ☐           | Not started |
| `fileUpload/`    | ☐              | ☐           | ☐                   | ☐           | Not started |
| `timecardRules/` | ☐              | ☐           | ☐                   | ☐           | Not started |
| `aiData/`        | ☐              | ☐           | ☐                   | ☐           | Not started |
| `accounts/`      | ☐              | ☐           | ☐                   | ☐           | Not started |

---

_Last updated: see git history — run Cursor diagnostic after each sprint to keep in sync._
