# Onboarding Agent — web

Angular 20 frontend (standalone components, signals) for the onboarding API. Spanish UI. It covers the plan's flow:
project, role, team (domain) and initiative selection, the four-step onboarding route, a chat with follow-ups, answers
with `[S#]` citations, a local evidence viewer and useful / not useful feedback.

**Scope and limits.** Verified in mock mode only (build, 13 unit tests, and a manual walkthrough in the browser). It has
**not** been run against the real API or Bedrock: the .NET SDK pinned by `global.json` (10.0.400) was not available on the
machine where this was built. No streaming (the API returns one JSON), no login screen and no `/api/gaps` screen.

## Prerequisites

- Node 20.19+, 22.12+ or 24+ (Angular 20). Built with Node 24.18.0 and npm 11.
- For the real API: the backend running on port 5006 (`scripts/start.ps1`).

## Run

From the repo root, in Windows PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/start-web.ps1          # against the API on :5006
powershell -ExecutionPolicy Bypass -File scripts/start-web.ps1 -Mock    # fixtures, no API needed
```

Then open http://localhost:4200. Without the script: `cd web; npm ci; npx ng serve [--configuration mock]`.

### How the token works

The API needs `Authorization: Bearer <JWT>`. `start-web.ps1` gets the dev token with `Get-DevToken` (the same one
`start.ps1` uses) and puts it only in the environment of the dev server process. `proxy.conf.js` adds it to every `/api`
call, so the browser never sees it and it is not in the bundle. It is not printed. If the token expires or is wrong, the UI
shows a 401 message: restart `start-web.ps1`.
The proxy also reads `ONBOARDING_API_URL` (default `http://localhost:5006`).

### Mock mode

`--configuration mock` replaces `src/environments/environment.ts` with `environment.mock.ts` (`useMock: true`) and the app
uses `MockOnboardingApi` instead of `HttpOnboardingApi`. Fixtures only: a supported answer with one citation, an
"insufficient" answer when the question mentions `presupuesto` or `budget`, and a simulated 502 when it contains `[error]`
(to see the retry button).

## Layout

| Path | What it holds |
|---|---|
| `src/app/core/models.ts` | Types mirroring `OnboardingAgent.Core/Contracts/Contracts.cs`. |
| `src/app/core/onboarding-api.ts` | Abstract API and injection token; `http-onboarding-api.ts` and `mock-onboarding-api.ts` implement it. |
| `src/app/core/session-store.ts` | Signals: selection, route, conversation, evidence, feedback. A new selection starts a new conversation. |
| `src/app/core/citations.ts` | Splits an answer into text and chips. Only ids returned in `sources` become chips. |
| `src/app/core/history.ts` | Last 6 turns sent as `history`; the assistant turn is the answer text. |
| `src/app/core/api-error.ts` | `ProblemDetails` to a readable error; 429, 502 and 504 are retryable. |
| `src/app/features/*` | `selection`, `route`, `chat`, `answer`, `evidence` components. |

## Languages

An ES / EN switch in the top bar changes the whole UI, the onboarding route, the initiative names and the language of the
next answers (sent to the API as `language`). The choice is kept in the browser (`localStorage`, key
`onboarding.language`); without one it follows the browser language (Spanish if it starts with `es`, English otherwise).
Switching keeps the conversation: earlier answers stay in the language they were written in.

UI texts are in `src/app/core/i18n.ts` (`es` and `en` dictionaries; a test fails if a key exists in only one). Texts that
come from the API (route steps, suggested questions, answers) are localized by the backend.

## Look and feel

Enterprise AI-assistant layout: a white top bar, a navy sidebar (context selectors and the onboarding route as a numbered,
connected path) and a light main area where the assistant is the center. Design tokens (CSS variables) are at the top of
`src/styles.css`: navy `#17243a`, sidebar `#202e45`, blue `#3975e8`, page `#f7f9fc`, border `#e0e6ef`, success `#16a085`,
12 px radius, 8 px spacing. No code or library from `HTGPlus.Web.UI` is used.

- **Main area:** a welcome (title, subtitle, description), the prompt card and four themed cards of starter questions
  (`features/suggestions`, texts in `i18n.ts`) while there is no conversation; then a chat with the user's messages on
  the right and the assistant's answers (avatar, status, citations, feedback) on the left, with the prompt card fixed at
  the bottom and a "searching" indicator while it waits.
- **Agent activity:** while a question is answered, a card in the conversation lists the real steps the backend reports
  (`POST /api/chat/stream`, Server-Sent Events read with `fetch`, see the backend README): understanding, searching,
  ranking, writing and checking the citations, each with its status, how long it took and a colored badge per kind of
  source found (Confluence, Jira, Repository, Manual). It is open while it runs or fails, folds to one line when it ends and
  can be opened again. The same badges mark the sources under each answer, in the evidence panel and in the route's documents.
  Cancelling (new conversation or another selection) closes the stream. `ng serve --configuration mock` replays a scripted
  sequence with short pauses (mock only); a message containing `[error]` fails at the ranking step.
- **Welcome screen order:** welcome, onboarding progress, prompt, starter questions, recommended next steps, project at a glance
  (`features/home/home-cards.ts`). What is real and what is not:
  - *Onboarding progress* is derived in the browser (`core/progress.ts`): the backend keeps no progress. A milestone counts as
    done when the user marks it in the route or asks one of its suggested questions; steps without documents are not counted.
    Stored in `localStorage` (`onboarding.progress.v1`), per project, role, team and initiative.
  - *Recommended next steps* are rule-based (next undone steps of the route, asking the agent with the step's first suggested
    question), not generated by the AI. The card says so.
  - *Project at a glance* reads `GET /api/projects/{id}/knowledge`: documents, passages, repositories (from GitHub URLs),
    teams, and one row per source type with the number of indexed documents. "Azure DevOps" is listed as "Not connected":
    there is no integration. Nothing is queried live from Confluence, Jira or repositories.
  - *Sources & trust* under each answer: type badge, title, repository / Confluence space / Jira project and file path when
    the URL shows them (`core/source-info.ts`), and a link to the original only when the source has a web URL.
- **Small screens (960 px and below):** the sidebar becomes a drawer (menu button, backdrop, Escape or the close button).
- **Icons:** Lucide-style line icons drawn inline in `src/app/shared/icon.ts` (no icon package).
- **Wordmark:** "Moltres Agent · Powered by Cast & Crew" is plain text plus an SVG cross (`app.html`, `app.css`), in the grey
  and copper colors sampled from the HTG logo; it is not an image of the official Cast & Crew logo and it is never hidden,
  only shrunk. `public/favicon.ico` still comes from `HTGPlus.Web.UI`.
- **Font:** the stack starts with Roboto but loads no web font, so machines without Roboto fall back to Helvetica or Arial.
- The starter questions are fixed texts; the corpus may not cover all of them (the answer then says so).

## Behavior worth knowing

- `insufficient` is shown as a normal answer. A 502, 504, 429 or network failure is shown as an API error with a retry
  button, never as missing evidence.
- A step with `status: "pending"` says there is no approved documentation yet; nothing is filled in.
- An external link appears only when the source has a `url`. Otherwise the evidence opens in the local viewer.
- `sourceUpdatedAt` of htg-server documents is the import date, not the git date; the viewer says so.
- The message limit (2000 characters) and the history limit (6) match the API's defaults.

## Commands

```powershell
cd web
npm ci
npx ng build            # production build
npx ng test --watch=false --browsers=ChromeHeadless
```

## Personal and admin views

- **Personal:** the home card "Your onboarding journey" now also shows level (1 to 4), day, questions asked and pace, read from
  `GET /api/me/progress`. Marking a step or asking one of its suggested questions also sends the steps to `PUT /api/me/progress`
  (`SessionStore.syncProgress`); a failure is silent and the browser copy keeps working. Only the steps are sent, never the questions.
- **Admin:** when `GET /api/me` says `isAdmin`, the top bar shows "My path / Admin". The admin view (`features/admin/admin-view.ts`,
  helpers in `core/admin.ts`) lists everyone's level, steps, day and pace, filters by team and role, and averages the people shown.
  Rows flagged `sample` are made-up demo people and say so. With `-Mock` the mock user is an admin and the table has fixtures; against
  the real API the local dev token is an admin because of `Progress:AdminUsers` in `appsettings.Development.json`.
- **Verified:** build, 52 unit tests, and a manual walkthrough against the real API (marked a step, saw it in the personal card and in the
  admin table, filtered by team). Not verified: small screens for the new top-bar switch and the admin table, and a non-admin token in the browser
  (the API returns 403, covered by an API test).