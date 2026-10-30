# Demo script: Moltres Agent (10 minutes = 2 overview + 6 live demo + 2 Q&A)

**Status (2026-10-29).** Every question below was run once against the real API (Bedrock, 21 indexed documents) and answered
in 5 to 13 seconds with the status listed. **Not rehearsed on stage, not recorded**, and no human has reviewed the answers:
read each answer and its sources before using it. Run everything live; a saved answer is a backup and must be labeled as one.

## Before you go on stage (15 minutes before)

- [ ] Terminal 1, from the repo root: `powershell -ExecutionPolicy Bypass -File scripts/start.ps1`. Stop any older API first
      (port 5006). The warning about the clipboard is harmless.
- [ ] `http://localhost:5006/api/health`: `"status": "ok"` and `"configured": true` (21 documents). `"auth"` reads
      `awsCredentials` even though the Bedrock key is in use; trust a real question, not that label.
- [ ] Terminal 2: `powershell -ExecutionPolicy Bypass -File scripts/start-web.ps1`, then open `http://localhost:4200`.
- [ ] Ask one throwaway question to warm up and prove the Bedrock key is valid (an expired key shows "The model could not
      answer", 502).
- [ ] Language switch on ES. Browser zoom so the route panel and the chat are both readable on the projector.
- [ ] Close notifications and other tabs. Keep Swagger (`/swagger`) and the saved sample `eval/evidence/sample-chat-response.json` as a fallback.
- [ ] The API allows **10 questions per minute per user**. This script asks 8 in 6 minutes: do not add more.

## 0:00-2:00 Overview (3 slides or one page, about 40 seconds each)

1. **Problem (0:00-0:40).** HTG knowledge is spread across Confluence, Jira and repositories. A new team member depends on
   senior people to know what to request, what to read and who owns what. Every team and every project repeats it.
2. **Solution (0:40-1:20).** Moltres Agent: pick a project, a role (Developer, QA, Leader), a team and an initiative and get a
   four-step onboarding route (overview, accesses and tools, local setup, domain questions). Then ask questions in Spanish or
   English; every answer cites its sources and opens the original passage. When the documents do not cover a question, it says so.
3. **Approach (1:20-2:00).** Documents (Confluence copies, Jira tickets, repo docs, a manual access checklist) become a
   Markdown corpus with metadata. Per question: Claude Haiku rewrites the query and re-ranks the candidates found by a keyword
   (BM25) index; Claude Sonnet answers with a structured reply; the backend rejects any citation that was not retrieved.
   Read-only: it cannot run commands or change systems. AWS Bedrock, .NET 10 API, Angular 20 UI.

Do not claim time saved: there is no study. Say "we propose a pilot with new hires".

## 2:00-8:00 Live demo

Changing the project, role, team or initiative **starts a new conversation**. Changing the language keeps it.

| Time | Do | Question (ES / EN) | Expect | Say |
|---|---|---|---|---|
| 2:00-2:30 | Open the app. Show the top bar, the four selectors and the ES / EN switch. Project HTG, role Developer, team General. | none | Four steps, all "Listo" | One route per role, team and initiative. Pending steps are labeled, never filled in. |
| 2:30-3:15 | Step 2 "Accesos y herramientas": click the suggested question. Open a citation chip. | ¿Qué accesos debo solicitar para trabajar en HTG? / Which accesses do I need to request to work on HTG? | `supported`, 8 s, cites the access checklist | Ten accesses and the tools to install, requested by ticket. It also says the document does not say who grants each one. The chip opens the source, version and date. |
| 3:15-4:30 | Pick initiative `H2G-6893` (the team switches to Silverwing on its own). Step 4: click the suggested question. Open the evidence. | ¿Qué cambió en H2G-7165? / What changed in H2G-7165? | `supported`, 7 s, cites the Jira ticket and the design | Technical answer with citations to the ticket and the design document. The panel shows the full document and says it is a snapshot. |
| 4:30-5:15 | **Optional.** Type the first question, then the follow-up. | ¿Qué tickets tiene el Excel Import? then ¿Qué cambia el último que mencionaste? | First `partial` (11 s), follow-up `supported` (11 s) about H2G-7060 | `partial` is honest: the corpus has no full ticket list. The follow-up understands "the last one". |
| 5:15-6:00 | Switch role to QA. Show the route change. Click the QA question. | ¿Qué escenarios debo probar en el Excel Import? / Which scenarios should I test in the Excel Import? | `partial`, 13 s, says there is no test plan | The role changes depth. It admits the QA strategy is not in the documentation: that is a gap to fill, not something to invent. |
| 6:00-6:30 | Switch role to Leader. | ¿Qué equipo es responsable de H2G-6893? / Which team is responsible for H2G-6893? | `supported`, 5 s, "Silverwing" | Answers with the team, never with people. |
| 6:30-7:00 | Type the no-evidence question. | ¿Cuál es el presupuesto anual del proyecto? / What is the project's annual budget? | `insufficient`, 5 s, no sources | It does not make up a number and points to who could know. |
| 7:00-7:30 | Click EN. The page and the route change language. Type the English question. | What does the Executive Officer Payroll Excel Import do? | `supported`, 8 s, answer in English | Spanish or English for the page and for the answers, even if the sources are in English. |
| 7:30-8:00 | Project selector: CAPS (or AICOE), ask the overview question. Show the "Pendiente" steps. | What is CAPS? / ¿Qué es AICOE? | `partial` (CAPS) or `supported` (AICOE), 7 to 8 s | Other projects come in as overview only: we have no access to their code yet, and the pending steps say so. |

Cuts if you are late: first the optional follow-up (0:45), then the CAPS / AICOE step (0:30).
While an answer loads (5 to 13 s), keep talking: say what is being searched and what to look at in the answer.

## 8:00-10:00 Q&A: honest answers

| Likely question | Answer |
|---|---|
| How do you avoid made-up answers? | Facts come only from retrieved passages; the model must cite them; the backend rejects a citation id that was not retrieved and retries once. Below that, the answer is `partial` or `insufficient`. It catches invented ids, **not** a claim that a real source does not support: that needs human review, which is not done yet. |
| Why keyword search and not embeddings? | We only have Claude models on Bedrock, and Claude does not produce embeddings. Haiku rewrites the question (also translating terms, since the corpus is in English) and re-ranks the BM25 candidates. |
| Is the data safe? | Only an approved corpus is indexed: no personal data (names, emails and similar were removed, names inside free text need a read-through), secrets stay out of the repository, and the key lives encrypted per user. There are no per-document permissions in this MVP. The Onboarding Documents pages are still **pending approval** for Bedrock and are not in the active corpus. |
| How do you keep the documents fresh? | Today they are snapshots with a version and a date. A sync from Confluence through the Atlassian MCP server is built, with a fallback to the Markdown copies, but it was **not tested against real Confluence** (it needs an API token that IT must enable). |
| How good is it? | We defined 15 evaluation cases. The last full run (before the latest corpus changes) **did not pass** the gate on role questions (4 of 9), because the documents they need are outside the approved scope; latency was within 15 s in 14 or 15 of 15 questions. It was not re-run after the changes, and no human reviewed the answers. Today we ran the 10 demo questions once. |
| What value does it bring? | We did not measure it. We propose a pilot with new hires, measuring time to resolve questions. |
| What does it not do? | No streaming, no code citations with line numbers (a few code excerpts are in the text), no per-document permissions, no live connectors during a chat, and no actions: it is read-only. |
| Can it scale to other projects? | Yes: each document names its project. AICOE and CAPS are already in as overview only. |
| Cost and latency? | Three model calls per question (rewrite, re-rank, answer); 5 to 13 seconds measured today; a limit of 10 questions per minute per user protects the Bedrock quota. |

## If something fails

- **"The model could not answer" (502)** is shown as an API error with a Retry button, never as "no evidence". Retry once. If it
  repeats, the Bedrock key is probably expired: stop and say so; do not pretend.
- **The page shows "The API returned an error (500)" and no teams:** the API is not running. Start it with `start.ps1`.
- **Port 5006 already used:** another API is running. Stop it, then start again.
- **No network or no Bedrock:** run `powershell -ExecutionPolicy Bypass -File scripts/start-web.ps1 -Mock`. The mock
  uses canned fixtures: say clearly that it is a recorded backup, not the live system.
- **Slow answer:** keep narrating; the limit is 45 seconds, after which a retryable error appears.

## Checklist: two full walkthroughs (human)

Run twice in a row, one per presenter, with a stopwatch. Mark only what you observed.

- [ ] Start with `start.ps1` and `start-web.ps1` from new terminals; health is `ok`.
- [ ] The overview takes 2:00 or less; the live demo 6:00 or less; leave 2:00 for Q&A.
- [ ] Each question answers in 15 s or less; note the time.
- [ ] Open the source of every citation you show; the passage supports the sentence.
- [ ] The no-evidence question says the documentation does not cover it.
- [ ] A forced error (stop the API) shows an error with retry, not "no evidence".
- [ ] The ES / EN switch changes the page, the route and the next answer.
- [ ] Walkthrough 1 passed: ____  Walkthrough 2 passed: ____  Date and names: ____

## Backup recording

Record only after both walkthroughs pass. Label it "backup recording", show the environment and the cited documents. Not done.
