# Fix Version & Environment Convention

### Read this before you start any ticket. Every time.

> Getting this wrong means running DB scripts against the wrong server,
> triggering an Azure release to the wrong environment, or merging into
> the wrong branch. None of these are easy to undo.

---

## The complete mapping

📋 **Current fix version numbers:** [ACTIVE_FIX_VERSIONS.md](ACTIVE_FIX_VERSIONS.md)

The table below shows the rules per environment type. The actual version numbers that map to each environment are in `ACTIVE_FIX_VERSIONS.md` — check there first.

| Environment type | Branch FROM          | Open PRs into                        | DB Scripts                   | Azure Release                  |
| ---------------- | -------------------- | ------------------------------------ | ---------------------------- | ------------------------------ |
| **UAT**          | `development`        | `development` + `release`            | No                           | Auto on merge to both branches |
| **QA1**          | minor feature branch | minor feature branch + `development` | No                           | Auto on merge to both branches |
| **QA2**          | `development`        | `development`                        | ✅ dev + QA1 first           | Auto on merge to development   |
| **QA (major)**   | major feature branch | major feature branch                 | ✅ dev + QA1 + QA (skip QA2) | Auto on merge to major branch  |

---

## How Azure builds work

Azure builds are **automatic on merge**. You do not trigger them manually.

```
You merge PR → Azure detects the merge → Build starts automatically
                                        → If build passes → Release is available
                                        → You promote the release to the target environment
```

Your job as a developer:

1. Merge the correct branch
2. Verify the Azure build passes
3. For DB-script fix versions: run scripts manually before promoting the release
4. Promote the Azure release to the correct environment

---

## Step-by-step by fix version

---

### UAT Release

**No DB scripts required.**

```bash
# 1. Branch from development
git checkout development && git pull origin development
git checkout -b H2G-XXXX-yourname-short-description

# 2. Do your work, commit, push
git push origin H2G-XXXX-yourname-short-description

# 3. Open TWO separate PRs from the same branch:
#    PR #1 → target: development
#    PR #2 → target: release
#
#    Both PRs get the same title: H2G-XXXX: Description
#    Append [development] and [release] to distinguish them.
#    Example:
#      "H2G-5138: Add payroll calculation [development]"
#      "H2G-5138: Add payroll calculation [release]"

# 4. Merge development PR first, then release PR
# 5. Azure builds automatically on each merge
# 6. Verify both Azure builds pass
# 7. Promote the UAT release in Azure
```

**Checklist:**

- [ ] PR #1 targets `development` — reviewed and approved
- [ ] PR #2 targets `release` — reviewed and approved
- [ ] `development` PR merged first
- [ ] `release` PR merged after
- [ ] Both Azure builds passed
- [ ] UAT release promoted in Azure

---

### QA1 Release

**No DB scripts required.**

```bash
# 1. Branch from minorfeature_1.0
git checkout minorfeature_1.0 && git pull origin minorfeature_1.0
git checkout -b H2G-XXXX-yourname-short-description

# 2. Do your work, commit, push
git push origin H2G-XXXX-yourname-short-description

# 3. Open TWO separate PRs from the same branch:
#    PR #1 → target: minorfeature_1.0   (primary review)
#    PR #2 → target: development         (sync — keeps development up to date)
#
#    PR #1 title: "H2G-XXXX: Description [QA1 - minorfeature_1.0]"
#    PR #2 title: "H2G-XXXX: Description [sync - development]"
#    In PR #2 description, link to PR #1: "Mirror of #123"

# 4. Get PR #1 approved and merge it first
# 5. Then merge PR #2 (development sync — usually fast-approval)
# 6. Azure builds automatically on each merge
# 7. Promote the QA1 release in Azure
```

**Checklist:**

- [ ] PR #1 targets `minorfeature_1.0` — full review, approved
- [ ] PR #2 targets `development` — references PR #1, approved
- [ ] PR #1 merged first (primary)
- [ ] PR #2 merged after (sync)
- [ ] Azure build on minorfeature_1.0 passed
- [ ] QA1 release promoted in Azure

---

### QA2 Release

**⚠️ DB scripts required — execute BEFORE promoting the Azure release.**

```bash
# 1. Branch from development
git checkout development && git pull origin development
git checkout -b H2G-XXXX-yourname-short-description

# 2. Do your work, commit, push
git push origin H2G-XXXX-yourname-short-description

# 3. Open ONE PR → target: development
```

**DB script execution order (before promoting Azure release):**

You must run the DB scripts against the lower environments before QA2 receives the change.

```
Server order — execute in this exact sequence:
  Step 1: Development server    → run scripts → confirm in PR comment
  Step 2: QA1 server            → run scripts → confirm in PR comment
  Step 3: Merge the PR          → Azure build triggers automatically
  Step 4: Verify Azure build passes
  Step 5: Promote QA2 release in Azure
```

**How to know which server to connect to:**
Check the Azure environment configuration for the `development` pipeline.
The server connection strings are in the Azure release variables per environment.
If you are unsure which server corresponds to which environment: ask the Tech Lead before running any script.

**Checklist:**

- [ ] PR targets `development` — reviewed and approved
- [ ] DB scripts identified and listed in PR description
- [ ] Scripts executed on Development server — confirmed in PR comment (who + when)
- [ ] Scripts executed on QA1 server — confirmed in PR comment (who + when)
- [ ] PR merged
- [ ] Azure build passed
- [ ] QA2 release promoted in Azure

---

### QA Major Release

**⚠️ DB scripts required — execute BEFORE promoting the Azure release.**
**This is a major release. Notify the Tech Lead before merging.**

```bash
# 1. Branch from majorfeature_2.0
git checkout majorfeature_2.0 && git pull origin majorfeature_2.0
git checkout -b H2G-XXXX-yourname-short-description

# 2. Do your work, commit, push
git push origin H2G-XXXX-yourname-short-description

# 3. Open ONE PR → target: majorfeature_2.0
```

**DB script execution order:**

```
Server order — execute in this exact sequence:
  Step 1: Development server    → run scripts → confirm in PR comment
  Step 2: QA1 server            → run scripts → confirm in PR comment
  Step 3: QA server             → run scripts → confirm in PR comment
  Step 4: QA2 server            → ✖ SKIP — QA2 is excluded for major versions
  Step 5: Tech Lead approval before merging
  Step 6: Merge the PR          → Azure build triggers automatically
  Step 7: Verify Azure build passes
  Step 8: Promote QA release in Azure
```

**Checklist:**

- [ ] PR targets `majorfeature_2.0` — full review + Tech Lead approval
- [ ] DB scripts listed in PR description
- [ ] Scripts executed on Development server — confirmed (who + when)
- [ ] Scripts executed on QA1 server — confirmed (who + when)
- [ ] Scripts executed on QA server — confirmed (who + when)
- [ ] QA2 explicitly skipped — noted in PR description
- [ ] Tech Lead notified and approved merge
- [ ] PR merged
- [ ] Azure build passed
- [ ] QA release promoted in Azure

---

## How to document DB scripts in your PR

Add this block to the PR description whenever DB scripts are required:

```markdown
## 🗄️ DB Scripts

**Script files:**

- `migrations/V17_5_0__add_payroll_period_column.sql`

**Execution log:**
| Environment | Server | Executed by | Date & time | Status |
|---|---|---|---|---|
| Development | [server name] | | | ☐ Pending |
| QA1 | [server name] | | | ☐ Pending |
| QA | [server name] | | | ☐ Pending (QA major only) |
| QA2 | — | — | — | ✖ Skipped (QA major) |
```

Update each row with your name and the date/time as you execute each script.
This is the audit trail — do not skip it.

---

## What to do when the fix version is missing from Jira

```
1. Do not create a branch.
2. Do not write any code.
3. Post in Teams: "H2G-XXXX has no fix version — who can set it?"
4. Wait for a confirmed fix version before doing anything.
```

Guessing the fix version is not acceptable. A wrong fix version means running
DB scripts against the wrong server or releasing to the wrong environment.

---

## Quick reference

```
UAT      → from: development        PRs: development + release          DB: no
QA1      → from: minor branch       PRs: minor branch + development     DB: no
QA2      → from: development        PR:  development                    DB: dev + QA1
QA major → from: major branch       PR:  major branch                   DB: dev + QA1 + QA (skip QA2)

Azure builds automatically on merge. You promote the release in Azure.
Current version numbers → ACTIVE_FIX_VERSIONS.md
```
