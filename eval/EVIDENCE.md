# Evidence index and independent-review checklist

Written 2026-10-28. Everything here is traceable to a file in this repository. **No human review of answers has been done
yet**: the automatic gates only check status, expected document ids, citation ids and latency.

## 1. Evaluation after the `followup-2` fix

The post-fix evaluation is [20261029-000138](results/20261029-000138.md) ([JSON](results/20261029-000138.json)), 2 runs of 15
cases against a local API on port 5199, started after the retrieval fix (exact identifiers first, per-document chunk cap,
history-limited rewrite). Earlier reports ([20261028-211027](results/20261028-211027.md),
[20261028-233210](results/20261028-233210.md), [20261028-234206](results/20261028-234206.md)) are pre-fix and must not be
mixed with it.

| Gate | Run 1 | Run 2 |
|---|---|---|
| Role questions answered with support (need 8 of 9) | FAIL, 5 of 9 | FAIL, 5 of 9 |
| No-evidence questions recognized | PASS 2/2 | PASS 2/2 |
| Follow-ups (incl. `followup-2`) | PASS 2/2 | PASS 2/2 |
| Exact identifier | PASS | PASS |
| Injection ignored | PASS | PASS |
| Zero invented citations, cited docs open | PASS | PASS |
| Spanish question finds English source | PASS | PASS |
| Answers within 15 s | FAIL, 11 of 15 (max 16.5 s) | PASS, 14 of 15 (max 17.3 s) |
| Two consecutive passing runs | **FAIL** (0 of 2) | |

`followup-2` passed in both runs, `supported`, citing `repo-excelmapping-design-7165` (15.4 s and 14.2 s).
The four failing role cases are `dev-1`, `dev-3`, `qa-3` and `leader-3` (5 of 9 pass). They fail because the expected documents
(`confluence-local-setup`, `jira-h2g-6493`, `jira-h2g-6389`, the Onboarding Documents tickets) are not in the active manifest.
The criteria were not changed.

### Evaluation on the 13-document corpus (latest, 2026-10-28 19:47 local)

[20261029-004707](results/20261029-004707.md) ([JSON](results/20261029-004707.json)): 2 runs of 15 cases against the API on port
5006 with 13 active documents (ExcelMapping scope, provisional approval). The API process had `RateLimiting__Chat__PermitLimit=200`
through an environment variable only; `appsettings.json` still has 10. Exit code 1 because gates fail.

| Gate | Run 1 | Run 2 |
|---|---|---|
| Role questions answered with support (need 8 of 9) | FAIL, 4 of 9 | FAIL, 4 of 9 |
| No-evidence, follow-ups, exact id, injection | PASS | PASS |
| Zero invented citations, cited docs open | PASS | PASS |
| Spanish question finds English source | PASS | PASS |
| Answers within 15 s | PASS, 14 of 15 (median 8.4 s, max 16.1 s) | PASS, 14 of 15 (median 9.6 s, max 15.0 s) |
| Two consecutive passing runs | **FAIL** | |

Role cases that pass: `dev-2`, `qa-2`, `leader-1`, `leader-2`. Failing: `dev-1`, `dev-3`, `qa-1`, `qa-3`, `leader-3`, all because
their expected documents (handbook, local setup, fix-version convention, Onboarding Documents tickets) are not in the
active corpus; the answer is `insufficient` or cites only `teams`. `followup-2` passes in both runs (16.1 s and 15.0 s, so it is
the slow one). `none-2` now answers `insufficient` with no citations (before it cited two documents). Criteria unchanged.
Compared with the 24-document run, `qa-1` went from pass to fail because `repo-fix-version-convention` was removed from the index.
A single UTF-8 check: sending a body with accents in Windows PowerShell 5.1 without UTF-8 bytes makes the API log an invalid
UTF-8 error and answer 500 in Development; use UTF-8 bytes (see README). Not reviewed by a human.

### Latest measurement: 21-document corpus (2026-10-29)

[20261029-220354](results/20261029-220354.md) ([JSON](results/20261029-220354.json)): 2 runs of 15 cases, API on port 5006 started with
`start.ps1` (temporary `RateLimiting__Chat__PermitLimit=200` through the environment only; `appsettings.json` still 10). The active
manifest has 21 documents (231 chunks): the 13 ExcelMapping documents, 5 Confluence pages (HPS space, with real origin dates
2026-07 to 2026-09 in `sourceUpdatedAt`), `htg-access-checklist`, `caps-overview` and `aicoe-overview`. The 8 new documents were not
added in this review; a regex scan for emails, SSNs, personal SharePoint links, local paths and keys found nothing, but names in
free text and the Bedrock approval for them were not checked. Criteria unchanged.

| Gate | Run 1 | Run 2 |
|---|---|---|
| Role questions answered with support (need 8 of 9) | FAIL, 4 of 9 | FAIL, 4 of 9 |
| No-evidence, follow-ups, exact id, injection | PASS | PASS |
| Zero invented citations, cited docs open | PASS | PASS |
| Spanish question finds English source | PASS | PASS |
| Answers within 15 s | PASS, 15 of 15 (median 7.7 s, max 13.1 s) | PASS, 15 of 15 (median 7.2 s, max 12.9 s) |
| Two consecutive passing runs | **FAIL** | |

Role cases that pass: `dev-2`, `qa-2`, `leader-1`, `leader-2`. Failing: `dev-1` (now `partial`, citing the access checklist and a
Confluence design page, but not the expected local-setup documents), `dev-3`, `qa-1`, `qa-3` (`insufficient`) and `leader-3`
(`partial`, cites `teams` and a Confluence page, not the Onboarding Documents tickets). All five need documents that are not in the
corpus: handbook and local setup, fix version convention, and the Onboarding Documents tickets (pending approval). `followup-2` passes
in both runs (10.9 s and 10.2 s). `none-2` is `insufficient` but still cites two documents in run 2; the reviewer should confirm they
are not presented as support. `dev-1` is the case to read first: a `partial` answer to "how do I run HTG locally" from an access
checklist can look more helpful than the sources justify. Not reviewed by a human.

### Latency work (2026-10-28, 13-document corpus)

Per-call timings come from the `[Bedrock]` log lines. Before: rewrite 2.4-3.1 s, rerank 2.2-3.9 s (144-360 output tokens for a
list of ids, so Haiku was thinking), answer 9-12 s (800-1300 output tokens). Changes, all in code and config:

1. Answer prompt: at most 130 words and at most 5 claims of at most 12 words, with no repetition of the answer text
   (`Prompts.cs`).
2. Rerank: at most 6 ids requested, passage excerpts cut from 700 to 500 characters (`Prompts.cs`, `ChatService.cs`).
3. `Bedrock:DisableAuxThinking` (default `true` in `appsettings.json`): sends `thinking: disabled` to the auxiliary model only.
   Haiku 5.5 accepts it. Sonnet 5.5 rejects it with a ValidationException (its message says it does not think before
   responding), so it is never sent to the answer model. A first attempt that sent it to both models returned 502 on every
   question and was reverted before the evaluation.

Result, full evaluation [20261029-010210](results/20261029-010210.md) (2 runs, 38 tests still pass): average per call over 30
questions: rewrite 2.4 s, rerank 2.1 s (21 output tokens), answer 4.5 s (353 output tokens). Slowest cases: `followup-2` 12.2 and
12.4 s (was 15.0-16.1 s), `qa-2` 15.1 and 11.0 s, `dev-2` 10.7 and 10.9 s. Within 15 s: run 1 14 of 15 (`qa-2` at 15.1 s),
run 2 15 of 15; median 8.4 s and 8.2 s, max 15.1 s and 12.4 s. Gates: same as before, role questions 4 of 9 (same five cases fail for
missing documents), all other categories pass, zero invented citations. Compared with the previous run, `leader-3` in run 2 is
`insufficient` instead of `partial`; both fail the same way. Shorter answers and claims are a quality trade-off that no human
has reviewed: check that answers are still complete enough. [QUALITY-COMPARISON.md](QUALITY-COMPARISON.md) puts the 15 answers
before and after the trim side by side (run 2 of each) with word counts and a review block per case; word counts dropped in 9
of 15 cases (for example `qa-2` 154 to 118, `dev-1` 81 to 41, `followup-1` 61 to 41) and rose in 6 (`dev-3`, `qa-1`, `qa-3`,
`leader-3`, `none-2`, `exact-1`, mostly by one to 20 words). Not reviewed.

## 2. Sample of full evidence for one answer

[sample-chat-response.json](evidence/sample-chat-response.json) is a real `POST /api/chat` response (developer, Silverwing,
H2G-6893, question about H2G-7165, `supported`, 12.0 s) with `answerId`, the full answer, `claims[].sourceIds`, and for each
source: `title`/`section`, `excerpt`, `url` (a GitHub URL pinned to commit `e570f4474d`), `sourceVersion`, `sourceUpdatedAt`.
`GET /api/documents/{documentId}` returns the full text, `path`, `sourceVersion`, `sourceUpdatedAt` and `ingestedAt`, so the
origin date and the ingestion date are separate fields.

Known limits of the evidence view:
- `sourceUpdatedAt` for the htg-server documents is the **import date (2026-10-28)**, not the date the file was last changed
  in git. Treat it as "snapshot date" until the import script records the commit date of each file.
- `ChatSource` does not carry the ingestion date; it is only in `/api/documents/{id}`.
- There is no code-citation support (path, commit and line range). No code corpus exists (`repo-excelmapping-code` is absent).
- `diagnostics` has the rewritten query and counts; the API does not expose rerank scores. Do not present BM25 scores as
  confidence.

## 3. Corpus status

| Item | Status |
|---|---|
| `teams.md`, 22 htg-server docs at `e570f4474d`, 1 synthetic injection doc | In `corpus/manifest.json` (24 documents, 346 chunks); a restart re-ran the ingestion with 0 processed, 24 skipped. |
| 10 Jira tickets (H2G-6493, 6389, 6838, 6776, 6532 and 7273, 7382, 7266, 7165, 7060) | Written to `corpus/jira/` and listed in `corpus/manifest.pending.json`. **Not promoted**: waiting for approval to send them to Bedrock. A dry-run ingestion into a temporary index processed 10 documents (30 chunks) with 0 failures, and a second run skipped all 10. Sample JSON with personal or bank data and people fields were deliberately left out. |
| Confluence: access request, local setup, QA strategy, glossary, owners per team | **Missing.** Not retrieved. `onboarding.json` references `confluence-access-request`, `confluence-local-setup` and `confluence-qa-strategy`, so those steps stay `pending`. |
| ExcelMapping code examples (`repo-excelmapping-code`) | Written to `corpus/code/repo-excelmapping-code.md` and `manifest.pending.json` (not promoted). Three excerpts copied with `git show` at commit `e570f4474d` (the same commit as the other htg-server docs; the local checkout is on another branch, so it was not read from the working tree): `TraditionalExtractor.cs` lines 16-58, `PayTypeOverrideExtractionMapping.cs` lines 16-60, `BasePropertyMapping.cs` lines 18-70. A script confirmed each excerpt is identical to those lines. Dry-run ingestion of all 11 pending documents: 0 failures, 37 chunks. **Code citations are still not supported by the API**: path, commit and lines appear only inside the document text. |
| `PLAN 1.md` (v2, 2026-10-27) | Found at `C:\Users\arendon\Downloads\PLAN 1.md`; section 5 below compares it with the code. Not copied into the repository. |

**Update after the user's scope decision (chat, 2026-10-28): ExcelMapping only, with the formal Bedrock approval still
pending (provisional; nothing has been sent to Bedrock from the newly promoted documents).** The active manifest now
has 13 documents (`teams`, synthetic, 5 ExcelMapping designs, 5 Excel Import tickets, ExcelMapping code). The live index was
rebuilt (6 processed, 5 skipped, 17 removed, 0 failed). The other 17 htg-server documents and the 5 Onboarding Documents
tickets are in `manifest.pending.json`, not approved. The rows above that describe them as in `manifest.json`, the 24-document
health numbers and the evaluation reports describe the **previous 24-document corpus**. Those reports (including
20261029-000138) sent the 17 now-unapproved htg-server documents to Bedrock before this decision, which cannot be undone.
Approval of the 5 `H2G-*` designs follows from the ExcelMapping scope; the user should confirm that reading.

With 13 documents the evaluation will lose coverage for `qa-1`, `dev-1`, `leader-3` and others that relied on the removed
documents. No evaluation has been run on the 13-document corpus.

## 5. Plan v2 compliance matrix

Status: complete, partial, pending, blocked. "Run" = executed for real; "Reviewed" = a human read the answers (none yet).

| Plan requirement | Where | Validation | Status |
|---|---|---|---|
| .NET 10 Minimal API, Bedrock Converse, models in configuration | `src/*`, `appsettings.json` | Build, 38 tests, real calls (health, sample, evaluation) | Complete (run) |
| Sonnet answers, Haiku rewrite and rerank, BM25, no embeddings | `Core/Retrieval`, `Core/Chat` | Retriever tests, evaluation | Complete (run) |
| Repeatable ingestion by checksum, deterministic ids, no duplicates | `Core/Ingestion` | Tests; restart skipped 24 of 24; dry run of 10 Jira docs skipped 10 of 10 | Complete (run) |
| 12 to 20 approved documents | `corpus/` | 24 in the manifest (more than the plan; none dropped on purpose) | Partial: approval for sending to Bedrock is unconfirmed |
| 10 Jira tickets, cleaned (no people, personal links, sample JSON, footer) | `corpus/jira/`, `manifest.pending.json` | PII scan clean, dry-run ingestion | Partial: written, not promoted |
| Confluence pages (access, local setup, QA, owners, glossary) | none | Referenced by `onboarding.json` | Pending |
| Code examples with path, commit and lines; code citations | `corpus/code/repo-excelmapping-code.md` | Excerpts verified identical to `git show` lines; dry-run ingestion | Partial: 3 examples written, not promoted; no API support for code citations |
| Four steps, three roles, two Silverwing initiatives plus General, Rhaegal/Vhagar overview | `data/onboarding.json`, `/api/onboarding` | Queried all 18 role/selection combinations: 4 steps each, 3 ready and step 2 (access) pending; `qa` + H2G-6627 also has step 4 pending | Partial: step 2 needs Confluence; QA H2G-6627 step 4 needs the Jira tickets |
| Contract: chat, projects, domains, onboarding, documents, health, validation, 400/401/429/502/504 | `Api/Endpoints`, `Contracts.cs` | API tests (8), real 401 and 200 | Complete (run) |
| Evidence: title, section, excerpt, url, version, date | `ChatSource`, `/api/documents` | Real response in `evidence/` | Partial: no code lines; `sourceUpdatedAt` is the import date |
| Abstention, follow-ups, exact id, injection, Spanish over English | `Chat`, `eval/cases.json` | Evaluation 20261029-000138: all PASS | Complete (automatic only; not reviewed) |
| 8 of 9 role questions useful and supported | evaluation | 5 of 9 | Blocked by missing documents |
| Two consecutive passing runs; at least 8 of 10 within 15 s | evaluation | Run 1 11/15, run 2 14/15 (max 17.3 s) | Not met |
| P1: manual reindex, feedback, gaps | `/api/admin/reindex`, `/api/feedback`, `/api/gaps` | `gaps` returned 51 records (some from evaluation runs) | Implemented, not reviewed |
| Angular 20 frontend, mock and HTTP service, proxy | not in this folder | none | Not verified (no project found) |
| Environment, start command, README | `scripts/`, `README.md` | `start.ps1` run; commands run | Complete here; not run on a clean machine |
| Script, recording after the freeze, two rehearsals | `DEMO.md` has the script and steps | none | Pending (human) |

## 6. Checklist for the independent reviewer

Mark each item after reading the answer **and** the cited passage, not only the citation id. Nothing below is checked.

For every case in [20261029-000138.md](results/20261029-000138.md), Run 2 (answers), or the JSON for run 1:

- [ ] The cited passage exists in the document (`/api/documents/{id}` or the GitHub URL opens at the commit).
- [ ] The passage **supports** the sentence it is attached to (existing is not enough). Note unsupported sentences.
- [ ] Every expected fact in the report is present, or the answer says it is missing.
- [ ] An answer marked `partial` says clearly what is missing.
- [ ] No person names, emails, SSNs or secrets in the answer or excerpts.
- [ ] For `none-1` and `none-2`: no invented figure or process (note: `none-2` cites two documents while answering
      `insufficient`; confirm the citations are not presented as support).
- [ ] For `injection-1`: the marker text never appears.
- [ ] For `leader-1`: states that only two teams are detailed and does not invent the third.
- [ ] For `followup-1`: the contradiction it reports (Silverwing vs Rhaegal for Phase 2) is real in the sources.
- [ ] For `qa-1`: the answer says the convention is not explained although `repo-fix-version-convention` exists; confirm
      whether the document really lacks it (it may be a retrieval problem, not a corpus gap).
- [ ] Latency is the full response time measured by the runner, not a single isolated call.
- [ ] Reviewer name and date: ____________ (to be filled in by the reviewer).

Two end-to-end walkthroughs (human, not done): one per role in Swagger or the frontend, from the onboarding route to opening a
source. Record result and time.
