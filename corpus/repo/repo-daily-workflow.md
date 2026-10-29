# H2G Developer Daily Workflow

### Complete loop: ticket → branch → develop → PR → Azure release

---

## Step 1 — Read the ticket before anything else

Open Jira and find **three things** before writing a line of code:

| Find this               | Where in Jira         | Why it matters                                               |
| ----------------------- | --------------------- | ------------------------------------------------------------ |
| **Fix Version**         | Right panel           | Determines branch, PR targets, DB scripts, Azure environment |
| **Acceptance Criteria** | Ticket description    | Defines what "done" means                                    |
| **Blockers**            | "is blocked by" links | Prevents starting something that can't be finished           |

**Fix version missing? → Post in Teams. Do not guess. Do not start.**

---

## Step 2 — Map fix version to your entire workflow

Before anything else, look up which environment your fix version targets. Every decision that follows (which branch to base from, how many PRs to open, whether DB scripts are required) depends on this.

> **Fix versions change every 1–2 weeks.** Do not rely on memory or old tickets.
> Monitor the **HTG Daily Scrum** Teams channel — updated fix versions are posted there whenever they change. If you are unsure which version is current for an environment, check that channel before doing anything else.

📋 **Current versions → environments → branches:** [ACTIVE_FIX_VERSIONS.md](ACTIVE_FIX_VERSIONS.md)

Open that file, find your fix version, and note: **Environment**, **Branch FROM**, **Open PRs into**, and **DB Scripts required**.

📖 Full checklists and server details per environment type: [FIX_VERSION_CONVENTION.md](FIX_VERSION_CONVENTION.md)

---

## Step 3 — Pull latest and create your branch

Use the table below to determine what to pull and how many PRs you will need to open.

| Fix version environment | Pull base from | PRs to open |
| ----------------------- | -------------- | ----------- |
| **UAT**  | `release` | **Two PRs** — one targeting `release`, one targeting `development` |
| **QA2**  | `development` | One PR targeting `development` |
| **QA1**  | `minorfeature_1.0` | One PR targeting `minorfeature_1.0` |
| **QA (major)** | `majorfeature_2.0` | One PR targeting `majorfeature_2.0` |

```bash
# UAT — base branch is release
git checkout release && git pull origin release

# QA2 — base branch is development
git checkout development && git pull origin development

# QA1 — free environment, no deploy restrictions; base is the current minor branch
git checkout minorfeature_1.0 && git pull origin minorfeature_1.0

# QA (major) — base is the current major branch
git checkout majorfeature_2.0 && git pull origin majorfeature_2.0
```

> For UAT fix versions you must open **two PRs from the same branch**: one into `release` and one into `development`. See Step 6 for details.

### Branch naming — non-negotiable

```
H2G-{TICKET}-{your-first-name}-{short-description}
All lowercase · hyphens only · your name identifies ownership
```

```bash
# ✅ Correct
git checkout -b H2G-5138-brayan-update-payroll-calculation
git checkout -b H2G-4200-ana-fix-employee-card-overflow
git checkout -b H2G-3900-carlos-add-hours-export

# ❌ Wrong
git checkout -b feature/payroll-fix       # no ticket, no name
git checkout -b H2G-5138                  # no name, no description
git checkout -b H2G-5138_brayan_payroll   # underscores not allowed
```

---

## Step 4 — Develop with automated quality tools

### What runs automatically

| Tool                | Trigger             | Does                                                  |
| ------------------- | ------------------- | ----------------------------------------------------- |
| Prettier            | File save (VS Code) | Formats the file                                      |
| ESLint              | File save (VS Code) | Highlights violations inline                          |
| Husky + lint-staged | `git commit`        | Lints + formats staged files, blocks commit on errors |

### Commit early and often

Format: `type(scope): short imperative description`

```bash
git commit -m "feat(payroll): add overtime rate calculation"
git commit -m "fix(employee): correct null check on profile date"
git commit -m "refactor(hours): extract usePayrollFilter hook"
git commit -m "chore(deps): upgrade MUI to v6.1.0"
```

Types: `feat` · `fix` · `refactor` · `chore` · `docs` · `test`

### Pre-commit blocks your commit? Fix it. Never skip it.

```bash
# Read the error — it shows file and line
# Fix the issue
git add src/the-fixed-file.tsx
git commit -m "your message"

# ❌ This defeats the entire system
git commit --no-verify
```

### Pull the base branch every morning

```bash
git fetch origin
git rebase origin/development  # or your feature branch base
```

---

## Step 5 — DB scripts

Check [ACTIVE_FIX_VERSIONS.md](ACTIVE_FIX_VERSIONS.md) — if the **DB Scripts** column shows **No**, skip to Step 6.

📖 Full execution order, server details, and documentation instructions: [DB_SCRIPTS.md](DB_SCRIPTS.md)

### Summary by environment

| Environment | Who runs in production-like env | Other environments |
| ----------- | ------------------------------- | ------------------- |
| **UAT** | Ask a workmate or the TL to run it in UAT | You run it on all QA servers + all development servers |
| **QA2** | — | You run it on all QA servers + all development servers |
| **QA (major)** | — | You run it on QA only + all development servers |

### Required for all cases — before running anywhere

1. Upload the script file to the **SharePoint release folder**, path [Information Technology > HTG Tech Team > HTG > {version folder}](https://castandcrew2.sharepoint.com/Information%20Technology)
You just replace the version number with your current release (e.g. 17.0.0, 14.7.0, etc.) and that's where the DBA Team expects to find the scripts before deployment. The deployment checklists in Confluence consistently reference that same SharePoint base path, just with the version folder changing per release.
2. Add the file name and execution order to the **deployment checklist** following your fix version e.g: [Deployment 17.5](https://castandcrew.atlassian.net/wiki/spaces/HPS/pages/4608065541/Readiness+Deployment+17.5.0)

Do not run any script before completing both steps above.

---

## Step 6 — Open your PR(s)

```bash
git push origin H2G-5138-brayan-update-payroll-calculation
# GitHub shows "Compare & pull request" — click it
```

### PR title

```
H2G-XXXX: Short description [branch-target]

Examples:
H2G-5138: Add overtime calculation to payroll [development]
H2G-5138: Add overtime calculation to payroll [release]
H2G-4200: Fix employee card overflow [minorfeature_1.0]
```

### UAT releases require two separate PRs

Open both from the same branch. Use `[branch-target]` in the title to distinguish them.
In the second PR description
Merge the feature branch PR first, then the development sync PR.

### Fill the PR template completely

Every section exists for a reason. The automated checks read it.

1. **Ticket & Release Info** — fix version + environment + target branch
2. **DB Scripts** — only for QA2 / QA major releases — fill execution log as you go
3. **What does this PR do?** — 2–4 sentences, what and why
4. **Localhost evidence** — screenshot or recording, always
5. **Self-review checklist** — check every item yourself

---

## Step 7 — After opening the PR

**What happens automatically (1–2 min):**

- GitHub Actions validates: branch name, fix version vs target branch, DB scripts section, ESLint, PR description
- Posts a quality report comment on the PR

**What Copilot does:**

- Reviews inline, comments on specific lines for standards violations (soft mode — comments only, does not block)

**What you do:**

1. Read the Actions quality report comment
2. Read all Copilot inline comments
3. Push fixes to your branch — the PR updates automatically
4. Only then request a human reviewer

**Rule: automated feedback first, human review second.**

---

## Step 8 — After merging

1. Azure build triggers automatically — check it passes in Azure Pipelines
2. If build fails: fix immediately, do not leave a broken build
3. Promote the Azure release to the target environment
4. For DB-script versions: confirm all scripts were already run before promoting
5. Verify the change in the target environment
6. Update the Jira ticket to IN QA, assign it to the tester or ask in daily what you should do.

---

## Step 9 — Code review (when you are the reviewer)

### What to focus on

- **Logic** — does it do what the ticket asks?
- **Architecture** — module boundaries, correct store patterns, and good practicies applyed?
- **Approachability** — can a junior developer read and maintain this in 6 months?

### Commenting style

```
# Blocking (must fix before merge)
"This function mutates its argument directly. Return a new object with spread — see FRONTEND_STANDARDS §10."

# Non-blocking (optional)
"nit: `result` could be named `activeEmployees` for clarity."
```

### Approval rules

- 1 approval required for all PRs
- Egineer Manager required for: stores, `shared/`, `theme/`, architecture changes
- `nit:` comments are optional — author decides

### Enforcing accountability with GitHub tooling

The following controls are either active or pending DevOps configuration. They exist because manual checklists alone are not enough.

**Active (GitHub Actions — `.github/workflows/`)**
- PR description validation: checks that fix version, target branch, and localhost evidence sections are filled
- Branch name validation: rejects names that don't match `H2G-{ticket}-{name}-{description}`
- ESLint check: blocks merge on lint errors

**Pending to setup (open tasks for the TL / DevOps)**
- **CODEOWNERS** — assign mandatory reviewers by path. Paths that need this:
  - `app/shared/` → Egineer Manager required
  - `app/styles/muiTheme/` → Egineer Manager required
  - `app/modules/*/store/` → Egineer Manager required
  - `HTGPlus.Handbook/` → Egineer Manager, Brayan.cruz required
- **Branch protection rules** on `release`, `development`, `majorfeature_2.0`, `minorfeature_1.0`:
  - Require status checks to pass before merge (Actions + Copilot)
  - Require at least 1 approved review
  - Dismiss stale reviews on new push
  - Do not allow bypassing required reviews (including admins)
- **DB script enforcement**: a GitHub Actions step that reads the PR description and fails if the DB Scripts section is empty when the fix version requires scripts

Until these are active, the rules above are still mandatory — the absence of automated enforcement is not permission to skip them.

---

## Quick reference card

```
Morning          → pull base branch before starting
Fix version      → check Jira BEFORE creating branch — stop if missing
Branch           → H2G-{ticket}-{name}-{description}
UAT              → open TWO PRs (one per target branch)
QA2 / QA major   → run DB scripts manually before promoting release
Fix versions     → change every 1–2 weeks; watch HTG Daily Scrum channel (updates are posted there)
Commit           → feat(scope): short description
Pre-commit fails → fix the error, never --no-verify
PR: always       → ticket + fix version + localhost evidence
After PR opens   → fix Actions + Copilot before requesting human review
After merge      → verify Azure build + promote release + verify in environment
```
