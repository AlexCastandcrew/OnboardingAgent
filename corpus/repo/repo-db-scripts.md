# DB Scripts — Execution Guide

> Check [ACTIVE_FIX_VERSIONS.md](ACTIVE_FIX_VERSIONS.md) to confirm whether your fix version requires DB scripts.
> If the **DB Scripts** column shows **No** — you can ignore this document.

DB scripts must be executed **manually by you** against the correct servers **before** you promote the Azure release. The Azure build triggers automatically on merge — you still need to promote it.

---

## Before running any script — required for all environments

1. **Upload the script file** to the SharePoint release folder for your version.
2. **Add the file name and execution order** to the deployment checklist (include any notes on how to run it or dependencies between scripts).

Do not execute any script until both steps above are done. This is the audit trail — skipping it will block the release.

---

## Execution order by environment

### UAT release

```
1. Upload script to SharePoint release folder
2. Add to deployment checklist
3. Ask a workmate or the Tech Lead to run the script in UAT
4. You run the script on all QA servers
5. You run the script on all development servers
6. Merge the PR → Azure auto-build starts
7. Verify build passes
8. Promote UAT release in Azure
```

> You are responsible for QA and development servers. UAT must be run by a second person — do not run it yourself.

### QA2 release

```
1. Upload script to SharePoint release folder
2. Add to deployment checklist
3. Run the script on all QA servers
4. Run the script on all development servers
5. Merge the PR → Azure auto-build starts
6. Verify build passes
7. Promote QA2 release in Azure
```

### QA (major) release

```
1. Upload script to SharePoint release folder
2. Add to deployment checklist
3. Run the script on the QA server
4. Run the script on all development servers
5. Get Tech Lead approval
6. Merge the PR → Azure auto-build starts
7. Verify build passes
8. Promote QA release in Azure
```

For the correct server connection details per environment: check Azure release variables.
If unsure which server maps to which environment: **ask the Tech Lead before running any script**.

---

## How to document a script in your PR

Every script that touches a database must be documented in the PR description under the **DB Scripts** section. Fill in each row as you execute — do not batch-fill after the fact.

```markdown
| Script file name | Target server | Executed by | Date/Time |
| ---------------- | ------------- | ----------- | --------- |
| add_column_payroll_v17.sql | Development | Brayan Cruz | 2026-04-10 10:30 |
| add_column_payroll_v17.sql | QA1          | Brayan Cruz | 2026-04-10 10:45 |
```

### Permanent vs one-time scripts

| Type | What to do |
| ---- | ---------- |
| **Permanent** (schema changes, seed data) | Add the file to the `SQL/` folder in the repo and include it in your PR |
| **One-time** (data fixes, backfills) | Upload to SharePoint only; do not commit to the repo |

---

## Accountability and enforcement

Skipping steps is not acceptable. The following controls enforce this:

- The PR description template has a required **DB Scripts** section. A GitHub Actions workflow validates that it is filled before merge is allowed on versions that require scripts.
- The deployment checklist in SharePoint is reviewed by the Tech Lead before every release promotion. A missing entry blocks the release.
- For UAT scripts: the second person who runs the script must leave their name and timestamp in the PR description. An undocumented UAT execution is treated as a not-executed script.

---

## Quick reference

```
All environments   → upload to SharePoint + add to deployment checklist FIRST
UAT                → second person runs in UAT; you run on all QA + dev
QA2                → you run on all QA + all dev
QA (major)        → you run on QA + all dev; get TL approval before merge

Permanent scripts  → commit to SQL/ folder in PR
One-time scripts   → SharePoint only, do not commit

Current version numbers → ACTIVE_FIX_VERSIONS.md
```
