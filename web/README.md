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

The styles imitate the HTG web app (`HTGPlus.Web.UI`, default theme): white top bar, dark `#23272d` side menu, `#f6f6f6`
page, `#1d89cf` primary blue, `#555` text, 2 px radii. Only colors and layout were copied; no code or library from that
project is used. All styles are in `src/styles.css` (CSS variables at the top). The header wordmark "Moltres Agent ·
Powered by Cast & Crew" is plain text plus an SVG cross in `src/app/app.html` and `app.css`, in the grey and copper colors sampled from the
HTG logo (it is not an image of the official Cast & Crew logo). `public/favicon.ico` still comes from `HTGPlus.Web.UI`. The
font stack starts with Roboto but loads no web font, so machines without Roboto fall back to Helvetica or Arial.

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
