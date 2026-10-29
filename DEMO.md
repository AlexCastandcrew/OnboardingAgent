# Demo script (10 minutes) and rehearsal checklist

Based on section 13 of `PLAN 1.md`. **Not rehearsed, not recorded.** Questions below passed the automatic checks in
[eval/results/20261029-000138.md](eval/results/20261029-000138.md) but no human has reviewed the answers; read each answer
and its sources before using it on stage. Run everything live; a saved answer is a backup and must be labeled as one.

Before starting: `powershell -ExecutionPolicy Bypass -File scripts/start.ps1` (needs the saved Bedrock key), check
`http://localhost:5006/api/health` shows `ok`, `configured: true`. No frontend exists in this repository, so use Swagger
(`/swagger`, Authorize with the token on the clipboard) unless the Angular app is available.

| Time | What to show | Question or action | Case that backed it |
|---|---|---|---|
| 0:00–1:00 | Problem: scattered knowledge | none | n/a |
| 1:00–2:00 | Scope: 1 project, 13 approved ExcelMapping documents, what is pending | `/api/health`, `/api/projects/htg/domains` | n/a |
| 2:00–4:00 | Developer, Silverwing, H2G-6893 | Ask "¿Qué tickets tiene el Excel Import?", then the follow-up "¿Qué cambia el último que mencionaste?" with that history (as in `followup-2`; the cited document must be the H2G-7165 design) | `followup-2`, sample in `eval/evidence/` |
| 4:00–5:30 | QA route and answer | Onboarding route for `qa`; ask about Excel import scenarios; **note the answer is `partial`** | `qa-2` |
| 5:30–6:30 | Leader | "¿Qué equipo es responsable de H2G-6893?" (names a team, not people) | `leader-2`, `exact-1` |
| 6:30–7:15 | No evidence | Annual project budget question | `none-1` |
| 7:15–8:15 | Architecture and limits | Bedrock, BM25 + Haiku rerank, citations validated by id | README |
| 8:15–9:15 | Measured results | Post-fix evaluation: 5 of 9 role questions, latency 11 and 14 of 15 within 15 s. State that the gates are **not** met yet | `EVIDENCE.md` |
| 9:15–10:00 | Next pilot | Pending Confluence, code examples, human review. Do not claim reduced onboarding time (no study) | n/a |

Do not demo while pending: access-request step, local-setup question (`dev-1` returns a partial answer from the wrong
document), Onboarding Documents ticket questions (`dev-3`, `qa-3`, `leader-3`) until the Jira tickets are promoted.

## Checklist: two full walkthroughs (human)

Run twice in a row, one per presenter. Mark only what you observed.

- [ ] Start with `start.ps1` from a new terminal; health is `ok`.
- [ ] Developer, QA and Leader routes load with four steps; pending steps are labeled pending.
- [ ] Each scripted question answers within 15 s; note the time.
- [ ] Open the source of every citation; the passage supports the sentence.
- [ ] The no-evidence question says the documentation does not cover it.
- [ ] A forced error (stop the API or use an expired token) shows as an API error with retry, not as "no evidence".
- [ ] Walkthrough 1 passed: ____  Walkthrough 2 passed: ____  Date and names: ____

## Backup recording

Record only after the product is frozen and both walkthroughs pass. Label it "backup recording", show the environment and
the cited documents. Not done.
