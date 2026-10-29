# Active Fix Versions

> **This is the only file you edit when fix versions change.**
> All other documents reference environment types (UAT, QA1, QA2, QA) — never specific version numbers.
> Update the table below and commit. Done.

---

**Last updated:** 2026-04-07

| Fix Version | Environment | Branch FROM        | Open PRs into                      | DB Scripts                   | Azure triggers on         |
| ----------- | ----------- | ------------------ | ---------------------------------- | ---------------------------- | ------------------------- |
| `17.0.0`    | UAT         | `development`      | `development` + `release`          | No                           | merge to both branches    |
| `17.1.0`    | QA1         | `minorfeature_1.0` | `minorfeature_1.0` + `development` | No                           | merge to both branches    |
| `17.5.0`    | QA2         | `development`      | `development`                      | ✅ dev + QA1                 | merge to development      |
| `18.0.0`    | QA          | `majorfeature_2.0` | `majorfeature_2.0`                 | ✅ dev + QA1 + QA (skip QA2) | merge to majorfeature_2.0 |

---

## How to update

1. Change the version numbers in the table above.
2. Update the **Last updated** date.
3. Commit with message: `chore(handbook): update fix versions YYYY-MM-DD`

That is all. No other file needs to change.
