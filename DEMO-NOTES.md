# Demo notes: Moltres Agent (10 min = 2 + 6 + 2)

Speaker notes. The full script with the rehearsal checklist is in [DEMO.md](DEMO.md). Every question below was run once
against the real API on 2026-10-29 (5 to 13 seconds each); no human has reviewed the answers yet, so read them before using them.

## Before you start
- API and web running, health `ok`, one warm-up question answered.
- Language ES, project HTG, role Developer, team General.
- Limit: 10 questions per minute. This script uses 8.

---

## 0:00-2:00 Overview

**Problem (0:00-0:40)**
> "When someone joins a team, the knowledge they need is scattered: Confluence, Jira, repositories. They don't know what to request, what to read, or who owns what. They end up depending on senior people, and every new hire repeats the same questions."

**Solution (0:40-1:20)**
> "Moltres Agent is an onboarding assistant. You pick a project, your role (Developer, QA or Leader), a team and an initiative, and you get a four-step onboarding route: overview, accesses and tools, local setup, and questions for your area. Then you can ask in Spanish or English. Every answer cites its sources, and when the documentation doesn't cover a question, it says so instead of guessing."

**Approach (1:20-2:00)**
> "We turn the documents into a Markdown corpus with metadata. For each question, one Claude model rewrites the query and re-ranks the candidates from a keyword index, and a second one writes the answer with citations. The backend rejects any citation that wasn't actually retrieved. It's read-only: it can't run commands or change any system. It runs on AWS Bedrock, a .NET API and an Angular front end."

*Don't claim time saved. Say: "we'd measure that in a pilot with new hires."*

---

## 2:00-8:00 Live demo
Changing project, role, team or initiative starts a new conversation. Changing the language keeps it.

**2:00-2:30 The app**
- **Do:** show the top bar, the four selectors and the ES/EN switch.
- **Say:** "One route per role, team and initiative. Steps without documentation are marked *Pending*; we never fill them in."

**2:30-3:15 Accesses (General, Developer)**
- **Do:** step 2, click *"¿Qué accesos debo solicitar para trabajar en HTG?"*. Open a citation chip.
- **Expect:** `supported`, ~8 s.
- **Say:** "It lists the ten accesses and the tools to install, all requested through a ticket. Notice it also says the document doesn't tell us who grants each access. The chip opens the exact source, version and date."

**3:15-4:30 A technical question**
- **Do:** pick initiative `H2G-6893` (the team switches to Silverwing on its own). Step 4: click *"¿Qué cambió en H2G-7165?"*. Open the evidence.
- **Expect:** `supported`, ~7 s, cites the Jira ticket and the design document.
- **Say:** "It answers from the ticket and the design document, with citations. The panel shows the full document and tells you it's a snapshot."

**4:30-5:15 Follow-up (optional, cut if late)**
- **Do:** type *"¿Qué tickets tiene el Excel Import?"*, then *"¿Qué cambia el último que mencionaste?"*.
- **Expect:** first `partial` (~11 s), then `supported` (~11 s) about H2G-7060.
- **Say:** "The first is partial because the corpus has no full ticket list, and it says so. The follow-up understands 'the last one'."

**5:15-6:00 QA role**
- **Do:** switch to QA. Ask *"¿Qué escenarios debo probar en el Excel Import?"*.
- **Expect:** `partial`, ~13 s, says there is no test plan.
- **Say:** "The role changes the depth. It admits the QA strategy isn't in the documentation. That's a gap to fill, not something to invent."

**6:00-6:30 Leader role**
- **Do:** switch to Leader. Ask *"¿Qué equipo es responsable de H2G-6893?"*.
- **Expect:** `supported`, ~5 s, "Silverwing".
- **Say:** "It answers with the team, never with individual people."

**6:30-7:00 No evidence**
- **Do:** ask *"¿Cuál es el presupuesto anual del proyecto?"*.
- **Expect:** `insufficient`, ~5 s, no sources.
- **Say:** "It doesn't make up a number. It says the documentation doesn't cover it and suggests who could know."

**7:00-7:30 English**
- **Do:** click EN (page and route change). Ask *"What does the Executive Officer Payroll Excel Import do?"*.
- **Expect:** `supported`, ~8 s, answer in English.
- **Say:** "Page and answers in Spanish or English, even though the sources are in English."

**7:30-8:00 Other projects**
- **Do:** select CAPS (or AICOE). Ask *"What is CAPS?"* / *"¿Qué es AICOE?"*. Show the Pending steps.
- **Expect:** `partial` (CAPS) or `supported` (AICOE), ~7-8 s.
- **Say:** "Other projects come in as overview only. We don't have their code yet, and the pending steps say exactly that."

*While an answer loads (5-13 s), keep talking: what's being searched, what to look for in the answer.*

**Closing line (8:00)**
> "So: role-aware onboarding, answers you can verify, and honest 'I don't know'. Next step would be a pilot with new hires."

---

## 8:00-10:00 Q&A: prepared answers

- **How do you avoid made-up answers?** "Facts must come from retrieved passages and be cited. The backend rejects a citation id that wasn't retrieved. Otherwise the answer is partial or insufficient. It does not catch a claim that a real source doesn't support, so a human review is still needed, and that's not done yet."
- **Why keyword search, not embeddings?** "We only have Claude models on Bedrock, and Claude doesn't produce embeddings. One model rewrites the question, translating terms since the corpus is in English, and re-ranks the keyword results."
- **Is the data safe?** "Only an approved corpus is indexed, with personal data removed; names inside free text still need a read-through. Secrets stay out of the repo and the key is stored encrypted per user. There are no per-document permissions in this MVP, and the Onboarding Documents pages are still pending approval for Bedrock."
- **How do documents stay fresh?** "Today they're snapshots with version and date. A Confluence sync through the Atlassian MCP server is built, with a fallback to the Markdown copies, but it's not tested against real Confluence: it needs an API token IT must enable."
- **How good is it?** "We defined 15 evaluation cases. The last full run, before the latest corpus changes, did not pass our gate for role questions (4 of 9) because the documents they need are outside the approved scope. Latency was within 15 seconds in almost every question. We haven't re-run it, and no human has reviewed the answers. Today I ran the demo questions once."
- **What value does it bring?** "We didn't measure it. We'd propose a pilot with new hires and measure time to resolve questions."
- **What can't it do?** "No streaming, no code citations with line numbers, no per-document permissions, no live connectors during a chat, and no actions: it's read-only."
- **Can it scale?** "Yes: each document names its project. AICOE and CAPS are already in as overview only."
- **Cost and latency?** "Three model calls per question (rewrite, re-rank, answer), 5-13 seconds measured today, and a limit of 10 questions per minute per user."

---

## If something fails
- **"The model could not answer" (502):** click Retry once. If it repeats, the Bedrock key is probably expired. Say so; don't pretend.
- **"The API returned an error (500)":** the API isn't running. Start `scripts/start.ps1`.
- **No network or Bedrock:** `scripts/start-web.ps1 -Mock`. Say clearly it's canned fixtures, a backup, not the live system.
