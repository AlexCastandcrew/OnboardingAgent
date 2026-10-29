# H2G Developer Handbook

> The single source of truth for how we build, ship, and maintain the H2G frontend.
> Changes go through a PR.

---

## Reading order for new team members

| Step | Document                                               | What it covers                                                            |
| ---- | ------------------------------------------------------ | ------------------------------------------------------------------------- |
| 1    | [DAILY_WORKFLOW.md](DAILY_WORKFLOW.md)                 | The complete developer loop: ticket → branch → code → PR → release        |
| 2    | [FIX_VERSION_CONVENTION.md](FIX_VERSION_CONVENTION.md) | Environment mapping, DB scripts, Azure release promotion — deep reference |
| 3    | [FRONTEND_STANDARDS.md](FRONTEND_STANDARDS.md)         | How we write code: components, stores, styling, clean code, immutability  |
| 4    | [Web.UI developer guide](../HTGPlus.Web.UI/README.md)  | How the frontend is structured now, the daily dev loop, tests, safety net |

---

## Document index

### For daily work

| Document                                               | Purpose                                                                                 |
| ------------------------------------------------------ | --------------------------------------------------------------------------------------- |
| ⭐ [ACTIVE_FIX_VERSIONS.md](ACTIVE_FIX_VERSIONS.md)    | **The only file you edit when versions change** — current version → environment mapping |
| [DAILY_WORKFLOW.md](DAILY_WORKFLOW.md)                 | Step-by-step: from Jira ticket to deployed release                                      |
| [FIX_VERSION_CONVENTION.md](FIX_VERSION_CONVENTION.md) | Rules per environment type — branches, PRs, DB scripts, Azure                           |
| [DB_SCRIPTS.md](DB_SCRIPTS.md)                         | DB script execution order and PR documentation template (QA2 / QA major only)           |

### For writing code

| Document                                       | Purpose                                                                                 |
| ---------------------------------------------- | --------------------------------------------------------------------------------------- |
| [FRONTEND_STANDARDS.md](FRONTEND_STANDARDS.md) | Coding standards: folder structure, naming, components, MobX, MUI, SOLID, clean code    |
| [MOBX_CONVENTIONS.md](MOBX_CONVENTIONS.md)     | Full MobX reference: store structure, examples, runInAction rules, multi-store patterns |

### For architecture and migration

| Document                                               | Purpose                                                                    |
| ------------------------------------------------------ | -------------------------------------------------------------------------- |
| [Web.UI developer guide](../HTGPlus.Web.UI/README.md)  | **Current** structure (core/routes/features/shared), workflow, tests, safety net |
| [ARCHITECTURE_MIGRATION.md](ARCHITECTURE_MIGRATION.md) | Historical: the original shield-module plan (superseded; lazy-loading rationale still valid) |
| [MODULE_INVENTORY.md](MODULE_INVENTORY.md)             | Historical: state of `app/` before the TypeScript migration                |

### For design

| Document                                                 | Purpose                                                                                           |
| -------------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| [DESIGN_DOCUMENT_GUIDE.md](DESIGN_DOCUMENT_GUIDE.md)     | How to write a design document: summary, decisions only, trade-offs, scenarios, review checklist |

### For tooling and measurement

| Document                                                   | Purpose                                                                         |
| ---------------------------------------------------------- | ------------------------------------------------------------------------------- |
| [CURSOR_DIAGNOSTIC_PROMPT.md](CURSOR_DIAGNOSTIC_PROMPT.md) | Prompt to run a full codebase audit — use to establish and refresh the baseline |
| [METRICS_TRACKING.md](METRICS_TRACKING.md)                 | Health scorecard and violation counts — updated every 2 sprints                 |

---

## Automated enforcement

These files run automatically and enforce the standards without manual checks:

| File                                     | What it does                                                                               |
| ---------------------------------------- | ------------------------------------------------------------------------------------------ |
| `.github/workflows/pr-quality-check.yml` | Validates branch name, fix version, DB scripts section, ESLint, PR description on every PR |
| `.github/copilot-instructions.md`        | GitHub Copilot inline review rules — comments on violations in code review                 |
| `.github/pull_request_template.md`       | Pre-filled PR template with all required sections                                          |
| `HTGPlus.Web.UI/.eslintrc.js`            | ESLint rules for frontend TS/TSX (gated by the lint ratchet)                               |
| `HTGPlus.Web.UI/.prettierrc`             | Code formatting rules                                                                      |
| `HTGPlus.Web.UI/.husky/pre-commit`       | Lint ratchet + prettier (lint-staged) and the structure check on staged frontend files      |
| `yarn verify` (HTGPlus.Web.UI)           | Typecheck, lint ratchet, unit tests, build, structure check — see the developer guide       |

---

## How to update this handbook

1. Open a PR with your proposed change
2. Link the relevant Jira ticket if the change is driven by a specific incident or decision
3. Tech Lead reviews and approves
4. Changes take effect on merge
5. Notify the Team via daily or grooming meetings that new updated was added to the standard documentation.
6. Also put a message in the daily teams channel to provide the information.
