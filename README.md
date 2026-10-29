# AI Team Onboarding Agent — backend

.NET 10 Minimal API for the onboarding assistant (plan v2). It holds the onboarding routes and runs a RAG chat over
the project corpus. Answers come from Claude on AWS Bedrock and cite their sources.

**Scope and limits.** Backend only. It serves the onboarding routes and a RAG chat over a small corpus (13 approved documents today, all about ExcelMapping/Excel Import plus team overview).
It is a hackathon demo, not a production service: no embeddings, no streaming and no per-document permissions. The Angular
frontend in `web/` is verified in mock mode only; its integration with this API is **not verified**.
Answers are not reviewed by a human by default; see [eval/EVIDENCE.md](eval/EVIDENCE.md) for what was and was not checked.

Repo root: `C:\Users\arendon\Documents\Hackathon\Backend\OnboardingAgent`. It is not a Git repository.

## Prerequisites

- Windows 11 and Windows PowerShell 5.1 (the scripts use DPAPI and `Set-Clipboard`).
- .NET SDK 10.0.400 or a later 10.0.x (`global.json`, `rollForward: latestFeature`). `dotnet --version` printed `10.0.400`.
- A Bedrock API key for `us-east-1` with access to the two model ids below (or AWS credentials with the same access).
- No Docker, database server or Redis: the index is a local SQLite file in `.data/`.

## Quick start (local, with the PowerShell scripts)

Run everything from the repo root in **Windows PowerShell**. You need two terminals: one for the API and one for the
web app.

**1. Once per person and machine: save your Bedrock key** (encrypted for your Windows user, never in the repo):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/set-bedrock.ps1
```

Paste the key at the hidden prompt (nothing shows while you paste) and press Enter on the other prompts to keep the
defaults. It ends with `API key saved encrypted: True`. Details in [Bedrock](#bedrock).

**2. Terminal 1: start the API** (http://localhost:5006). It builds, creates a dev token (copied to the clipboard) and
passes the key only to the API process. The terminal stays busy while the API runs.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/start.ps1
```

Check http://localhost:5006/api/health: `"status": "ok"` and `"configured": true`. Add `-NoBuild` to skip the build on a restart.

**3. Terminal 2: start the web app** (http://localhost:4200). The first run installs the npm packages. It reads the dev
token itself and its proxy adds it to every `/api` call; it needs the API from step 2.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/start-web.ps1
```

Without the API (fixtures only): add `-Mock`. If Angular asks about sharing usage data, answer `N`.

**4. Use it:** open http://localhost:4200, pick project, role, team and initiative, and ask. Swagger stays at
http://localhost:5006/swagger as a fallback.

**Stop:** Ctrl+C in each terminal.

| Problem | Fix |
|---|---|
| `Port 5006 is already used ...` | Another API instance is running (a previous `start.ps1` or Visual Studio). Stop it first. |
| Build error `MSB3027` or "file is locked by OnboardingAgent.Api" | The API is still running and locks the `.exe`. Stop it (Ctrl+C or end the `dotnet` process) and build again. |
| `The argument 'scripts/start-web.ps1' ... does not exist` | You are not in the repo root. Run `cd` to the folder that contains `scripts/`, or use `../scripts/start-web.ps1` from `web/`. |
| `The API is not answering on port 5006` | Start `start.ps1` first, or use `-Mock`. |
| Chat shows "The model could not answer" (502) | Missing, wrong or expired Bedrock key. Run `set-bedrock.ps1` again. See [Troubleshooting](#troubleshooting). |
| Running from Visual Studio (F5) | The API gets no Bedrock key and chat returns 502. Use `start.ps1` for the demo. |
| The web app shows "The API returned an error (500)" and no teams | The API is not running (the web proxy answers 500 when it has no backend). Start it with `start.ps1`. |
| `Set-Clipboard : Requested Clipboard operation did not succeed` | Another program holds the clipboard. `start.ps1` now only warns and starts the API anyway; the clipboard token is just for Swagger. |

The web app is described in [web/README.md](web/README.md).

## Commands

From the repo root:

```powershell
dotnet restore OnboardingAgent.slnx
dotnet build OnboardingAgent.slnx --no-restore
dotnet test OnboardingAgent.slnx
```

Last run (2026-10-28): restore up to date, build succeeded with 0 warnings and 0 errors, **38 tests passed**
(Core 23, API 8, Evaluation 7), none failed.

## Run

One command, from the repo root (Windows PowerShell):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/start.ps1
```

Parameters: `-AwsProfile <name>` (only if you use AWS credentials instead of the saved API key) and `-NoBuild`. The script
blocks the terminal while the API runs; open another terminal for the calls below. It was run on 2026-10-28 and
`GET /api/health` answered `"status": "ok"`, `"auth": "apiKey"`, `"configured": true` (that run had the earlier 24-document
corpus; the corpus is now 13 documents, 17 chunks, and health was not re-run).

It checks the .NET SDK from `global.json` and that port 5006 is free, then builds the API. It creates a local dev
token if none is valid and **copies it to the clipboard**; the token is never printed. It warns when the Bedrock ids or
AWS credentials are missing, then runs the API on http://localhost:5006. `-NoBuild` skips the build. If scripts
are blocked on the machine, run it as `powershell -ExecutionPolicy Bypass -File scripts/start.ps1`.

Without the script: `dotnet run --project src/OnboardingAgent.Api --launch-profile http`.

- Port 5006 (`launchSettings.json`, profile `http`). Health: http://localhost:5006/api/health (no token).
  Swagger UI: http://localhost:5006/swagger (`/` redirects there). OpenAPI JSON: `/openapi/v1.json`.
- Stop: Ctrl+C in the terminal running `start.ps1` (it removes the decrypted key from that process). If it runs hidden,
  stop the `dotnet` process listening on 5006 (`Get-NetTCPConnection -State Listen -LocalPort 5006`).

### Ask a question and open the evidence

`start.ps1` copies the dev token to the clipboard. To use it from a script without printing it:

```powershell
. ./scripts/common.ps1
$h = @{ Authorization = "Bearer $((Get-DevToken).Token)" }
$body = @{ projectId='htg'; domain='silverwing'; initiative='H2G-6893'; role='developer'
           message='What does H2G-7165 change in the AI Data screen?'; history=@() } | ConvertTo-Json
# Send UTF-8 bytes: Windows PowerShell 5.1 would otherwise mangle accents (the API then fails to parse the body).
$r = Invoke-RestMethod -Method Post -Uri http://localhost:5006/api/chat -Headers $h -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
$r.evidenceStatus; $r.answer; $r.sources | Select id, documentId, section, sourceVersion, url
Invoke-RestMethod -Uri "http://localhost:5006/api/documents/$($r.sources[0].documentId)" -Headers $h   # full text, path, ingestedAt
```

A real response is saved in [eval/evidence/sample-chat-response.json](eval/evidence/sample-chat-response.json). Without the
header the API answers 401.

### How to read a response

- `evidenceStatus`: `supported` (sources back the answer), `partial` (some of it, and the answer says what is missing) or
  `insufficient` (the corpus does not cover it). `insufficient` is a normal answer, not an error.
- `sources[]` hold only cited passages: title/section, excerpt, `url` (null when there is no real link), `sourceVersion`
  (commit or snapshot) and `sourceUpdatedAt`. For htg-server documents `sourceUpdatedAt` is the import date, not the git date.
  `GET /api/documents/{id}` adds `ingestedAt`. Check that the passage supports the sentence, not only that it exists.
- A technical failure is an HTTP error (ProblemDetails 502 or 504), never `insufficient`. Show it as an API error and retry.
- `diagnostics` shows the rewritten query and counts. It is not a confidence score.
- On startup the API ingests `corpus/` (set `Corpus:IngestOnStartup` to false to skip).
- `src/OnboardingAgent.Api/OnboardingAgent.Api.http` has a sample call for every endpoint.

### Configuration check

Before it starts, the API checks every setting and lists **all** the problems at once, then exits with code 1:

```text
Configuration is invalid (Development):
  - Corpus:ChunkOverlapChars (5000) must be 0 or more and smaller than ChunkTargetChars (2000).
  - Chat:ContextChunks (50) must be between 1 and CandidateCount (20).
```

It checks:
- the corpus folder, manifest, `projects.json` and `onboarding.json` exist;
- numeric ranges and cross-field rules (chunk overlap below the target, context chunks no more than candidates,
  boosts of at least 1);
- the rate limit;
- the bearer settings: there must be signing keys or an Authority, plus an audience.

Missing Bedrock model ids are only a startup warning, so the API can run without Bedrock.

### Authentication

Every endpoint except `GET /api/health` needs `Authorization: Bearer <JWT>`; without it the API returns 401.
The JWT bearer handler reads issuer, audiences and keys from `Authentication:Schemes:Bearer`.

- Locally, `dotnet user-jwts` issues the tokens. It already wrote the issuer and audiences to
  `appsettings.Development.json` and keeps the signing key in user secrets (outside the repo). To get a token:

  ```bash
  dotnet user-jwts create --project src/OnboardingAgent.Api --name <your-name>
  ```

  `dotnet user-jwts list --project src/OnboardingAgent.Api` shows existing ones, `print <id> --output token` reprints one.
- In Swagger, click **Authorize** and paste the token (without `Bearer `). It is kept across page reloads.
- Against a real identity provider, set `Authentication:Schemes:Bearer:Authority` and `ValidAudiences` instead;
  no code change.

### Bedrock

Two ways to authenticate. Nothing goes in the repo either way.

1. **Bedrock API key (what the organization gave us).**

   **Save the key (once per person and machine):**

   ```powershell
   cd <repo folder>
   powershell -ExecutionPolicy Bypass -File scripts/set-bedrock.ps1
   ```

   The script asks four things:

   | Prompt | What to enter |
   |---|---|
   | `Bedrock API key (hidden)` | Paste the key (right-click or Ctrl+V) and press Enter. **Nothing shows while you paste; that is expected.** |
   | `Region` | Press Enter to keep `us-east-1` |
   | `Answer model ... (Sonnet)` | Press Enter to keep `us.anthropic.claude-sonnet-5-5` |
   | `Rewrite/rerank model ... (Haiku)` | Press Enter to keep `us.anthropic.claude-haiku-5-5` |

   It ends with `API key saved encrypted: True`. Press Enter on any prompt you don't have yet. You can run the script
   again later: an empty key prompt keeps the saved key, and an empty prompt keeps the current value.

   **Start the API with the key:**

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts/start.ps1
   ```

   **Check it:** `GET http://localhost:5006/api/health` must show `"configured": true`. `start.ps1` passes the key as
   `AWS_BEARER_TOKEN_BEDROCK`, so `"auth"` reads `"awsCredentials"` even though the key is used; prove it with a chat
   question, not with that label.

   **Replace the key (for example when it expires):** run `set-bedrock.ps1` again and paste the new key.

   **Remove the key from this machine:**

   ```powershell
   Remove-Item "$env:LOCALAPPDATA\OnboardingAgent\bedrock-api-key.txt"
   ```

   How it is stored:
   - **The key is never stored in plain text.** It is encrypted with your Windows account (DPAPI) in
     `%LOCALAPPDATA%\OnboardingAgent\bedrock-api-key.txt`, so only you, on this machine, can decrypt it.
   - The region and model ids are not secret; they go in the API's user secrets.
   - `./scripts/start.ps1` decrypts the key and passes it only to the API process (`AWS_BEARER_TOKEN_BEDROCK`), then
     removes it when the API stops. The client sends it to Bedrock as a bearer token.
   - **Running the API from Visual Studio (F5) or plain `dotnet run` skips `start.ps1`, so the API gets no key.**
     `/api/health` then shows `"auth": "awsCredentials"` and chat returns 502. Use `start.ps1` for the demo.
   - Each person saves their own key: the encryption is tied to the Windows user, so the file can't be shared.
   - The environment variable `Bedrock__ApiKey`, or the SDK's own `AWS_BEARER_TOKEN_BEDROCK`, also works.
     Never put the key in `appsettings*.json` or a `.env` file inside the repo.
2. **AWS credentials** (`AWS_PROFILE`, SSO session, env vars), used when no API key is set:
   `./scripts/start.ps1 -AwsProfile <profile>`.

`appsettings.json` already has the verified defaults: region `us-east-1`, `us.anthropic.claude-sonnet-5-5` for answers and
`us.anthropic.claude-haiku-5-5` for rewrite and rerank. Both were tested with a real call on 2026-10-28. Model ids are not
secret; override them per machine with `set-bedrock.ps1` if needed. Without ids, `/api/health` reports `degraded` and
`/api/chat` returns 502 with a clear message. `/api/health` also shows `models.auth` (`apiKey` or `awsCredentials`),
never the key.

Bedrock has no single global limit. Quotas (requests and tokens per minute) depend on the model, the region and the
account. The SDK retries throttling up to `Bedrock:MaxRetries` times, and the API's own limit is 10 questions per
minute per user (`RateLimiting:Chat`). Each question makes up to three model calls (rewrite, rerank, answer). Lower
the limit if the evaluation shows `ThrottlingException`.

## Layout

Same conventions as htg-server: `src/` and `tests/`, one `*.UnitTests` project per tested project, package
versions only in `Directory.Packages.props`, shared build settings in `Directory.Build.props`, and each library
registers itself through a `ServiceCollectionExtensions`.

| Path | What it holds |
|---|---|
| `src/OnboardingAgent.Api` | Host only: `Program.cs`, endpoints, Swagger, `appsettings.json`. |
| `src/OnboardingAgent.Core` | Domain logic: ingestion, SQLite index, retrieval, catalog, onboarding routes, chat pipeline, contracts. Defines `IClaudeClient`; knows nothing about AWS. |
| `src/OnboardingAgent.ExternalApi.Atlassian` | `IConfluenceClient` over the Atlassian Rovo MCP server (API token) and its options. |
| `src/OnboardingAgent.ExternalApi.Bedrock` | `IClaudeClient` on AWS Bedrock (Converse API) and its options. |
| `tools/OnboardingAgent.Evaluation` | Console runner for the 15 evaluation cases against a running API. |
| `tests/OnboardingAgent.Api.UnitTests` | Tests against the real host (WebApplicationFactory): auth on every endpoint, chat contract, 502 and 429. |
| `tests/OnboardingAgent.Core.UnitTests` | xUnit tests for Core, named `Method_Condition_ExpectedOutcome`. |
| `tests/OnboardingAgent.Evaluation.UnitTests` | Tests for the evaluation scorer and quality gates. |
| `eval/` | `cases.json`, the `results/` of each run, [EVIDENCE.md](eval/EVIDENCE.md) (summary and reviewer checklist) and `evidence/` (a real response). |
| `scripts/` | `start.ps1` (one-command start) and `eval.ps1` (runs the evaluation with the dev token). |
| `corpus/`, `data/` | Corpus Markdown and manifest; projects and onboarding routes. |
| `.data/` (gitignored) | Generated index, feedback and gaps. |

Build settings: .NET 10 (`global.json`), nullable on, warnings as errors, and XML comments turned on so the
Swagger page shows the DTO descriptions and examples.
## Ingestion

```bash
dotnet run --project src/OnboardingAgent.Api --no-launch-profile -- ingest
```

Optional `--corpus <folder>` and `--manifest <file>` override the configured paths. It prints processed, skipped, removed and failed documents, and exits with 1 if any failed. `POST /api/admin/reindex`
does the same from the local machine.

- Input: `corpus/*.md` plus `corpus/manifest.json`. The ingestion only knows this folder, not where files came from.
- A re-run skips documents whose checksum (text plus metadata) did not change. A changed document has its chunks
  replaced. A document that left the manifest is removed. Chunk ids are deterministic: `documentId#section[.part]`.
- It refuses paths outside the corpus folder and the file names in `Corpus:ExcludedPatterns` (`.env*`,
  `appsettings*`, `CLAUDE.md`, ...).
- Chunks: one per Markdown section. Sections over ~2000 characters (~500 tokens) are split with ~320 characters
  (~80 tokens) of overlap.

The manifest has one default `projectId`. A document can set its own `"projectId"` (for example `"caps"`) to be indexed under
another project; without it, it belongs to the manifest's project. Ingestion removes documents that left the manifest in
every project already indexed. The overview documents of AICOE and CAPS use this.

Manifest entry:

```json
{
  "documentId": "jira-h2g-7165",
  "path": "jira/H2G-7165.md",
  "title": "H2G-7165 ...",
  "sourceType": "jira",
  "sourceUrl": null,
  "sourceVersion": "snapshot-2026-10-27",
  "sourceUpdatedAt": null,
  "domains": ["silverwing"],
  "initiatives": ["H2G-6893"]
}
```

`sourceType` is one of `repo`, `confluence`, `jira`, `manual`, `synthetic`. Use `domains: ["general"]` for a document
that applies to the whole project.

## Building the corpus

Documents reach Bedrock only through `corpus/manifest.json`. New documents wait in `corpus/manifest.pending.json`,
which ingestion never reads, until they are approved for Bedrock and reviewed.

**1. Import the htg-server docs** listed in `corpus/sources/repo-docs.json` (22 today):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/import-repo-docs.ps1 -RepoPath "<path to htg-server>"
```

- It reads every file **from git** at `-Ref` (default `origin/majorfeature_2.0`), never from the working copy.
  Each source therefore has a fixed commit and a GitHub URL to that commit. It does not change the htg-server checkout.
  Run `git fetch` there first.
- It writes `corpus/repo/<documentId>.md` and `corpus/manifest.pending.json`.
- It applies `corpus/sources/redactions.json` (person names, emails). If a redaction no longer matches the source,
  the import fails instead of letting the data through, so review that document and update the file.
- It scans for emails, @mentions, phone and SSN-like numbers, personal SharePoint/OneDrive links and local user
  paths. Findings go to `.data/import-report.md`, which is gitignored because it quotes what it found.
  **The scan cannot detect person names**: review them by hand.

**2. Review.** Read `.data/import-report.md` and resolve each finding with a redaction, or note it as a false
positive (today only `example.com` samples and SQL `@status` parameters remain). Search the copies for names.

**3. Promote**, only after the approval to send these documents to Bedrock:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/promote-corpus.ps1
```

It moves the pending entries into `manifest.json`. The next start, or `POST /api/admin/reindex`, indexes them.

Status on 2026-10-28:
- **Approval for Bedrock: PROVISIONAL, formal approval still pending (user, 2026-10-28).** The user asked to include only the
  ExcelMapping documents for now and said the approval is still pending. No Bedrock call has used the newly promoted
  documents yet; do not run chat or the evaluation until the approval is confirmed. `manifest.json` now holds 13 documents:
  `teams`, the synthetic injection document, the 5 `repo-excelmapping-design-*` documents, the 5 Excel Import Jira tickets
  (H2G-7273, 7382, 7266, 7165, 7060) and `repo-excelmapping-code` (3 excerpts with path, commit `e570f4474d` and line ranges,
  copied with `git show`). The live index was rebuilt: 6 processed, 5 skipped, 17 removed, 0 failed (17 chunks).
- Everything else waits in `corpus/manifest.pending.json` (22 documents), **not approved**: the 17 other htg-server documents
  (commit `e570f4474d`, 6 redactions) and the 5 Onboarding Documents tickets (H2G-6493, 6389, 6838, 6776, 6532, copied
  read-only from Jira without people, sample JSON or personal links). Those 17 htg-server documents had already been sent to
  Bedrock by earlier evaluation runs, before this scope was set. The previous manifests are in
  `.data/manifest.before-excelmapping-scope.json` and `.data/manifest.pending.before-excelmapping-scope.json`.
- A dry run of any pending manifest into a temporary index (`Corpus__StateFolder` pointing at a temp folder,
  `ingest --manifest <absolute path>`) leaves the live index untouched.
- **Confluence copies (2026-10-29).** 14 pages of the HPS space "Architecture Design" tree were copied read-only to
  `corpus/confluence/*.md`, with authors, assignee columns, example SSNs and personal links removed (checked with a pattern
  scan; person names can only be reviewed by reading). The 5 ExcelMapping pages are in `manifest.json`; the 8 Onboarding
  pages are in `manifest.pending.json` (not approved for Bedrock yet). `confluence-externaldatamapper-page.md` (one
  sentence and a link) is not listed. Each entry has the page URL as `sourceUrl`. These copies are also the fallback if a
  live sync from Confluence is added later: it must never delete them when it fails.
- **AICOE and CAPS** are overview-only projects: one document each (`corpus/aicoe`, `corpus/caps`) with a per-document `projectId`.
- Still missing: the Confluence pages for access request, local setup, QA strategy, glossary and owners per team, and API
  support for code citations (path, commit and lines are only inside the document text).

## Confluence sync (MCP)

The app can refresh the Confluence copies in `corpus/confluence/` itself, through the Atlassian Rovo MCP server
(`src/OnboardingAgent.ExternalApi.Atlassian`, official `ModelContextProtocol` client). It runs on demand, not per chat question, and is
read-only: it only calls the page-read tool (`getConfluencePage`, Markdown).

**Enable it**
1. An Atlassian admin must allow API token authentication for the Rovo MCP server.
2. Create an API token for your account and run `./scripts/set-atlassian.ps1` (hidden prompt; the token is stored encrypted with
   DPAPI like the Bedrock key, the email goes in user secrets). Or set `Atlassian__Email` and `Atlassian__ApiToken` in the environment.
   The token is never read from `appsettings.json` and is never logged.
3. Run `./scripts/sync-confluence.ps1`, or `dotnet run --project src/OnboardingAgent.Api --no-launch-profile -- sync-confluence`
   (with the two variables set), or `POST /api/admin/sync-confluence` (bearer token, loopback only; `./scripts/start.ps1` passes the token to the API).

The pages are listed in `corpus/sources/confluence-pages.json` (page id and output file; the other fields mirror the manifest entry).
To add a page, add it there and in the manifest. Each page is sanitized before it is written: author, owner, reviewer and assignee lines and
columns, @mentions, emails, personal SharePoint/OneDrive links are removed, SSN/EIN-like values become placeholders and images become
`[diagram omitted]`. **Person names inside prose cannot be detected: review every page the sync reports as `synced` before sharing the corpus.**
After a sync that changed a page, ingestion runs as usual (checksums skip unchanged documents).

The result lists `synced`, `unchanged` and `failed` (page id and reason); the command exits 1 if anything failed or the sync did not run.

**Fallback.** With no token, a 401/403, a network failure or an unreadable page, the sync logs a `[Sync]` warning with the page id and reason,
leaves the existing `.md` file untouched (files are written to a temporary file and moved, nothing is deleted or truncated) and continues; an
authentication or connection failure stops the run and lists the remaining pages as not attempted. The API starts and answers from the
Markdown corpus with no Atlassian credentials at all.

**Not verified.** No run with a real token: the Atlassian token auth is not confirmed enabled. Verified only with a fake client (unit tests)
and with an invalid dummy token against the live server (it answered 401 and the sync reported it). Not confirmed: that the server's
`getConfluencePage` returns the same JSON shape (`content.nodes[0]`: title, body, lastModified, space) as the session tool the parser was written
against, and that a first sync produces text identical to the hand copies (the first sync may rewrite every page). Atlassian's docs list
`https://mcp.atlassian.com/v2/mcp` for API token auth with `Authorization: Basic base64(email:token)`; `Atlassian:Endpoint` can change it.
Service account keys (`Bearer`) are not supported.

## Endpoints

| Method | Path | What it does |
|---|---|---|
| GET | `/api/health` | Status, index size and configured model ids. No secrets. |
| GET | `/api/projects` | Projects: `htg` (full) plus `aicoe` and `caps`, which are overview only (one overview document each, no teams or initiatives; their code was not available). |
| GET | `/api/projects/{projectId}/domains?language=` | Teams with their initiatives and depth (`full` or `overview`). With `language=en` an initiative uses its `nameEn` when it has one. |
| GET | `/api/onboarding/{projectId}/{role}?domain=&initiative=&language=` | The four steps, with documents, links and suggested questions. With `language=en` each step uses its `en` texts, field by field, and falls back to Spanish. |
| POST | `/api/chat` | The plan's contract plus `language`, `answerId`, `claims` and `diagnostics`. `language` is `es` or `en`; the answer is written in that language whatever language the question or the sources use. Without it the answer follows the question's language. |
| GET | `/api/documents/{documentId}` | The document's text, metadata and sections, for the local evidence view. |
| POST | `/api/feedback` | Useful or not useful for an `answerId` (P1). |
| GET | `/api/gaps` | Questions answered with `insufficient` evidence (P1). |
| POST | `/api/admin/reindex` | Re-runs the ingestion. Only from the local machine. |
| POST | `/api/admin/sync-confluence` | Syncs the Confluence pages through MCP, then ingests. Only from the local machine; see Confluence sync. |

Errors are ProblemDetails: 400 for validation (unknown project, role, domain, an initiative outside its domain,
an empty or long message), 401 without a valid token, 429 over the rate limit, 502 when the model fails, 504 on
timeout (`Chat:RequestTimeoutSeconds`). A technical
error is never returned as `insufficient`.

### Rate limit

`POST /api/chat` allows `RateLimiting:Chat:PermitLimit` questions (default 10) per `WindowSeconds` (default 60)
for each user (the token's subject), or for each IP without a user. Over the limit it returns 429 with a
`Retry-After` header and a ProblemDetails body. This protects the Bedrock budget and quotas.

## Chat flow

1. Validate the request. Client history is untrusted: only `user`/`assistant` items, the last 6, at most 2000
   characters each.
2. Haiku rewrites the question as a standalone question and adds English and original-language keywords. This runs
   on every question, not only follow-ups, because the corpus is in English and BM25 does not cross languages. If
   it fails, the raw question is searched.
3. SQLite FTS5 BM25 finds 20 candidates for the project. Documents tagged with the initiative are boosted ×1.5 and
   documents tagged with the domain ×1.25. Nothing is excluded.
4. Haiku reranks the candidates and keeps up to 6. If it keeps none, the answer is `insufficient`. If the rerank
   call fails, the BM25 order is used.
5. Sonnet answers through a forced tool call (`answer`, `evidenceStatus`, `claims[].sourceIds`). The system prompt
   is on the server: sources are data, it answers in the question's language and it never names people.
6. The backend rejects ids that were not retrieved, and a `supported` or `partial` answer with no citation. It
   retries once, then returns 502. This catches invented ids, not claims a source does not support. The manual
   evaluation covers those.
7. `sources` contains only the cited passages. An `insufficient` answer stores the question (not the conversation)
   in `.data/gaps.jsonl`.

## Languages (Spanish and English)

The UI, the onboarding routes and the agent's answers are available in Spanish (`es`) and English (`en`). Any other value
of `language` is a 400. The corpus stays in English; when a source is quoted it keeps its original language.

- `data/onboarding.json`: each step has Spanish `title`, `goal` and `suggestedQuestions`, and an optional `en` block with
  the same three fields. A missing English field falls back to the Spanish one.
- `data/projects.json`: an initiative can have `nameEn`.
- `Prompts.LanguageRule`: the answer prompt fixes the language when the request has one.
- A new route step or initiative needs both languages; the UI check is the English route in the browser.

## Data and state

| Path | Content |
|---|---|
| `data/projects.json` | Project → teams → initiatives (snapshot 2026-10-27, no people). |
| `data/onboarding.json` | Steps per role, domain and initiative. The most specific route that defines a step wins. |
| `corpus/` | Markdown and the manifest. |
| `.data/` (gitignored) | `index.db`, `feedback.jsonl`, `gaps.jsonl`. Delete it to rebuild from scratch. |

An onboarding step whose documents are not in the index is `pending`. `onboarding.json` already uses the document ids
the corpus is expected to have (`jira-h2g-7273`, `repo-handbook-readme`, `confluence-access-request`, ...). Each step
becomes `ready` once T03 adds a document with that id to the manifest.

## Evaluation

`eval/cases.json` holds the 15 cases of plan section 10. There are 3 per role, 2 follow-ups, 2 without evidence,
1 with an exact identifier and 1 with instructions hidden inside a document. The last one uses
`corpus/synthetic/release-freeze.md`, a document labeled synthetic that tries to make the model print a marker.

With the API running (`./scripts/start.ps1`):

```powershell
./scripts/eval.ps1                 # 2 runs of all cases
./scripts/eval.ps1 -Runs 1 -Only leader-1,exact-1
```

The script passes the dev token through `ONBOARDING_EVAL_TOKEN` and removes it afterwards. To call the runner
directly, set that variable yourself and run `dotnet run --project tools/OnboardingAgent.Evaluation -- --base-url <url>`.

Options: `--cases`, `--out` (default `eval/results`), `--runs` (default 2), `--only id1,id2`. Each run writes
`<timestamp>.json` and `<timestamp>.md`. The exit code is 0 only when every gate passes. Reports are per run, with the
question, history, selections, full answer, status, cited documents, latency, errors and the automatic result. Manual review
is not recorded by the runner: use the checklist in [eval/EVIDENCE.md](eval/EVIDENCE.md). The full run makes about 90 paid
Bedrock calls (2 runs x 15 cases x up to 3 calls), and the default limit of 10 questions per minute can throttle it; if you
raise it for a run, use the environment variable `RateLimiting__Chat__PermitLimit` for that process only.

What the runner checks automatically, per case:
- HTTP 200 (a 502/504 is a failure, not an "insufficient" answer);
- the evidence status (`answered` = supported or partial, `insufficient`, `any`);
- that the answer cites at least one expected document;
- no `[S#]` without a source, and every cited document opens through `/api/documents/{id}`;
- no forbidden text (the injection marker).

Gates per run: at least 8 of 9 role questions pass, every no-evidence, follow-up, exact-id and injection case passes,
zero invented citations, at least one Spanish question finds its English source, and at least 10 answers with 80%
within 15 s (median and max in the report). A last gate needs two consecutive runs that pass.

Whether the answer states the right facts is a **manual review**. The Markdown report ends with a checklist of
expected facts and each answer. Cases marked `needsCorpus` fail until their documents are in the manifest.

Latest result, 13-document corpus, after the latency changes ([20261029-010210](eval/results/20261029-010210.md)): **does not
pass**. 4 of 9 role questions (the five failing ones need documents that are not approved or not imported), every other
category passes, zero invented citations. Latency: 14 of 15 (run 1) and 15 of 15 (run 2) within 15 s, median 8.4 s and 8.2 s,
max 15.1 s and 12.4 s. Before the changes it was 14 of 15 in both runs with a max of 16.1 s
([20261029-004707](eval/results/20261029-004707.md)). Details in [eval/EVIDENCE.md](eval/EVIDENCE.md).

Previous result, 24-document corpus, after the `followup-2` fix ([20261029-000138](eval/results/20261029-000138.md)): **did not
pass**. Both runs: 5 of 9 role questions (`dev-1`, `dev-3`, `qa-3`, `leader-3` fail for missing documents), follow-ups,
no-evidence, exact-id and injection cases pass, zero invented citations. Latency: run 1 had 11 of 15 within 15 s, run 2
had 14 of 15 (max 17.3 s). `followup-2` passed in both runs. No human review has been done.
## Tests

```bash
dotnet test OnboardingAgent.slnx
```

The API tests host the real pipeline with a scripted Claude client and JWTs signed by a test key. They check that
every `/api` endpoint except health returns 401 without a token, that a token for another audience is rejected,
the chat response shape, 502 on a model failure, and the per-user 429. The Core tests cover repeated ingestion without duplicates, changed and removed documents, excluded paths, chunk
overlap, exact ticket-key search, initiative/domain priority, citation retry and rejection, abstention with the gap
record, rewrite fallback, and onboarding route precedence.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `Port 5006 is already used by ...` | Another instance or Visual Studio is running. Stop it, then run `start.ps1` again. |
| `running scripts is disabled` | Use `powershell -ExecutionPolicy Bypass -File scripts/<name>.ps1`. |
| 401 | Missing, expired or wrong-audience token. Run `start.ps1` again (it renews the dev token) or `dotnet user-jwts create --project src/OnboardingAgent.Api`. |
| 429 | More than 10 chat questions per minute per user. Wait for `Retry-After`. |
| 502 | Bedrock failed: expired or missing key (`/api/health` shows `auth` and `configured`), a model id without access, or throttling. Re-run `set-bedrock.ps1` with a fresh key. |
| 504 | The answer took longer than `Chat:RequestTimeoutSeconds` (45). Retry. |
| `auth` is `awsCredentials` and chat fails | The API was started without `start.ps1` (Visual Studio or plain `dotnet run`), so it has no saved key. Use `start.ps1`. |
| Startup says `Configuration is invalid` | Fix every listed setting; the API exits with code 1. |
| Missing corpus or manifest at startup | Restore `corpus/` and `corpus/manifest.json`; the check lists the missing path. |
| `insufficient` for a topic you expect | The document may not be in the manifest (Jira and Confluence pending). Check `/api/gaps`. |

## Limitations and pending items

- The corpus has 13 approved documents (ExcelMapping scope). The other htg-server documents and the Onboarding Documents
  tickets are pending approval and the Confluence pages are not imported, so several onboarding steps stay `pending` and the questions that depend on them return `insufficient`.
- Bedrock region and both model ids were verified with real calls on 2026-10-28 (health, evaluation and a sample question).
- Rhaegal and Vhagar only have the team overview in `teams.md`; the answers say so and do not go deeper.
- The Angular app is in `web/` (see [web/README.md](web/README.md)). It was verified in mock mode (build, unit tests, a
  browser walkthrough) but **not against the real API**: the proxy that adds the bearer token and the error/retry paths
  with real responses are not verified. The API allows CORS from `http://localhost:4200`.
- Not verified from a clean machine: the instructions were run in this Windows account, with its saved credential and tokens.
- No claim is made about onboarding time saved.
- No embeddings: BM25 plus the Haiku rerank. `Chat:MinScore` is 0 until the evaluation calibrates it.
- No streaming. The response is one JSON object.
- Authentication only checks for a valid token; there are no roles or per-document permissions. Reindex also only accepts loopback calls.
