# External API Services (Redesign)

## Summary

**Why.** HTG calls other systems: Payroll, CAPS, and later a master data service. Each connection was built on its own. Four integration projects (CapsPay, CnCPayroll, StartPlus, StudioPlus) each carry their own copy of the login code, their own settings and their own registration. The calls are grouped by the system they go to: one Payroll client holds invoice, employee and control-number calls, and one CAPS service holds clients, projects, occupations, employees and batches. A project that needs one call references the whole client and every domain it touches. A fix to login or retry is made four times, and a fifth service means a fifth copy.

**The design.**
- One shared way to call any outside service, in `HTGPlus.ExternalApi`. It owns the token, the 401 refresh, the failure kind and the failure logging.
- Each service keeps its own login and message format in one small vendor project. Nothing else about it is shared.
- A call is a query or a command. A query is any read, whatever the HTTP method. A command is any write, with its own longer timeout.
- The transport never retries a call, apart from the one repeat after a 401 token refresh. Every failure carries a kind, and the caller decides: a screen shows the message, a message consumer sends the message back to the queue.
- A failed call is reported as a failure. It is never turned into an empty result.
- Domain projects never reference a vendor project. A domain adapter in the owning domain decides which calls its domain makes.

**What it gives the business.**
- A login fix is made once.
- Connecting a new system is one small project.
- A failed Payroll or CAPS call is logged with the payload the service owner asks for, so the failure can be reproduced from the log.

**What we need from others.**
- Nothing for Payroll: its source defines the status codes (section 5). A new service owner documents how its envelope reports a rejected request and a missing record.

**Risks we accept.**
- A momentary service blip reaches the caller at once. The caller or the message redelivery owns the retry.

**Scope.** The transport, the vendor projects for Payroll and CAPS, and how a domain adapter calls a service. The lookup of names for stored keys is a separate design, [External Lookup](../../HTGPlus.ExternalLookup/docs/external-lookup-design.md), built on this one. The screens that call a service are separate designs.

---

## 1. Problem

| Behavior | Consequence |
|---|---|
| Four integration projects each define their own auth handler, config class and registration | A fix to login or retry is made four times; a fifth service means a fifth copy |
| One Payroll client carries invoice, employee and control-number calls; one CAPS service carries clients, projects, occupations, employees and batches | A project that needs one call references the whole client and every domain it touches |
| Each client decides on its own whether to retry | A write can be repeated, and a read can fail on a blip with no retry |
| Payroll sends almost every call as POST, and each endpoint reports a rejection its own way: an HTTP 200, 400, 404 or 500 with a `status` object in the body | A caller that checks only the HTTP status, or only one of these shapes, treats a rejected request as a success |

## 2. Goals and non-goals

**Goals**

- One call path for every outside service, with each service's login and message format in one small project.
- A read and a write are different calls: a write has its own timeout and its own timeout message.
- Every failure is reported to the caller and logged once with what was sent.
- A domain project calls a service through its own adapter and never references a vendor project.

**Non-goals**

- Looking up names for stored keys, searching a list while the user types, or caching. That is [External Lookup](../../HTGPlus.ExternalLookup/docs/external-lookup-design.md).
- Retrying a call inside the transport. The caller decides from the failure kind.
- Changing what a service returns. The vendor project reads the service's envelope as the service sends it.
- Replacing a service's own business rules. A domain adapter still owns which calls its domain makes.
- DocuSign and the SMTP email path. They stay as they are.

## 3. Architecture

```
Domain adapter (in the owning domain: allocate a number, create a batch, lookup)
        |
Transport  (named service: authenticator, protocol, query / command)
        |
Vendor project (login, message format, error rules)
        |
Payroll  /  CAPS  /  master data service
```

| Part | Single job |
|---|---|
| Transport (`HTGPlus.ExternalApi`) | Send a request to a named service, authenticated, and report `Ok`, `NotFound` or `Failed` |
| Vendor project (`HTGPlus.ExternalApi.<Vendor>`) | Hold one service's login, message format and error rules |
| Domain adapter | Decide which calls its domain makes and map the result to its own types |
| Composition | Wire the HTG specifics for a host with one call |

## 4. Transport (`HTGPlus.ExternalApi`)

A service is registered by name with its own authenticator and protocol:

```csharp
services.AddExternalService("payroll", section).UseLoginToken(...).UseProtocol(...);
```

| Interface | Varies per service |
|---|---|
| `IExternalAuthenticator` | how a request is authenticated and what a 401 means |
| `IExternalProtocol` | how a body is written and a response read, including vendor envelopes |

| Call | Used for |
|---|---|
| `QueryAsync<T>` | any read, whatever the HTTP method |
| `CommandAsync<T>` | any write, with its own longer timeout |

| Outcome | Cause |
|---|---|
| `Ok` | the service accepted the request |
| `NotFound` | a 404, or the vendor protocol's documented not-found |
| `Failed` | a timeout, a 5xx, a failed login, a validation error |

Every `Failed` result carries a kind, so a caller decides retry, dead-letter or message without reading error text:

| Kind | Cause | Retry can help |
|---|---|---|
| `Transient` | cannot connect, timeout, 408, 429, 5xx | yes |
| `Rejected` | a validation message, any other 4xx | no |
| `Unauthorized` | 401 or 403, including a refused login | no |

`ExternalServiceException` carries the kind, so a consumer decides without reading status codes. The `Error` of a failed result is a plain sentence for the user, for example "The payroll service is temporarily unavailable. Please try again in a few minutes." The service's raw response is kept in `RawResponse`, SSN-masked, for the log only, so a gateway HTML page never reaches a user.

**The service's own text.** Every result carries `Message`: what the service said in the response, read by the vendor protocol and SSN-masked. A vendor protocol reads it from any response it can parse, so it is set on `Ok`, `NotFound` and `Failed` alike, and is null for a body with none (a gateway HTML page). A caller shows `Message` to the user and branches on it only when the text is the domain's own. For a failure the same text is the `Error`, so a failure never shows a generic sentence when the service said why. The protocol is told whether the call is a query or a command (`ExternalCallKind`), so a vendor can apply a rule that holds only for writes.

A 401 refreshes the token once and repeats the request; a second 401 is `Failed`. The token is cached per service and the login is serialized.

**Decision: the transport never retries; the caller decides from the failure kind.** A caller with a person waiting, such as a dropdown, shows the error and the user tries again. A message consumer, such as the search-index exporter, sends a `Transient` failure back to the queue so the message is redelivered, and dead-letters a `Rejected` one. A retry loop in the transport would hold a consumer thread asleep and make a person wait through it. The call type, not the HTTP method, separates a read from a write, because Payroll sends every call as POST.

**Decision: a command has its own, longer timeout, and a command that times out says the outcome is unknown.** A client that stops waiting does not stop the service. A command that times out may still be processed, so its message reads "The request may have been processed. Check before sending it again." and never "try again". A query that times out says "took too long to respond. Please try again."

**Trade-off:** a momentary service blip reaches the caller at once. A consumer recovers through redelivery, which the consumer's own design owns. A transient failure of a command always reaches the caller.

## 5. Vendor projects

One project per service. Domain projects never reference one; a host registers it.

| Piece | Payroll | CAPS |
|---|---|---|
| Authenticator | login token | login token; strips the `Basic ` prefix; sends a `User-Agent` |
| Protocol | the body is `{status, data}`; a validation message inside a 200, or an error status such as a 400 or 500, is `Failed`; a command whose `status.code` is not 0 and that has a message is `Failed` as well, because Payroll reports a rejected write as a 200 (a query with a non-zero code and no validation messages stays `Ok`, its caller reads the envelope); `Message` is the validation messages joined with "; ", otherwise `status.message`; a 404 whose body has a `status` object is `NotFound` (Payroll's "Record not found!"), and a 404 without one is `Failed` | adds `userId` and `source` to every body; `isSuccess` is a bool or an int; a not-found message inside a 200 is `NotFound` |
| Request rewrite | inserts the payroll year into a top-level `dbCode` | none |
| Registration | `AddPayrollApi(configuration)` | `AddCapsPay(configuration)` |

**Payroll reports a rejected write as a 200.** No other service here does this, so the rule is in `PayrollProtocol` and nowhere in the transport. A write that Payroll refuses comes back as HTTP 200 with `status.code` 2 or 3 and a message (for example "Unable to delete payroll batch 5" or "Invoice is posted."). For a command the protocol reads that as `Failed`, with Payroll's text as `Error` and `Message`, so the payload and the response are logged. For a query the same body stays `Ok`, because Payroll answers some reads with a non-zero code and data. A code 0 with a message ("Incomplete! No record is updated.", an invoice on hold) is `Ok` with the text in `Message`; the domain decides what to show. The approval endpoints return no `status` at all, so none of this applies to them.

**Payroll year.** The caller sends the plain dbCode. The transport inserts the year after the three-letter prefix (`PAY` becomes `PAY26`, `PAYPA` becomes `PAY26PA`) and leaves a code that already has one. The Payroll project owns the year: `AddPayrollApi` registers a source that reads the year once from the database (the `commonDatabase` of the US region record), keeps it in memory for the life of the process, and shares it with every call. It is the calendar year in production and a database-set year in a non-production environment that rehearses year end. A host adds nothing for it.

**Write status rule.** Payroll's status object is `{code, message, validationMessages}`. A read tolerates a non-zero code with no validation message and leaves the decision to the adapter, usually a default. A write fails on any status error. `PayrollStatus.ErrorText` is the validation messages joined with "; ", otherwise `message` when the code is not 0, otherwise null; a write adapter fails when it is not null.

**Rejected request versus missing record.** Payroll answers per endpoint, with `status.code` 0 Ok, 1 Prompt, 2 Warning, 3 Error, 4 NotFound, 5 Unauthorized, 6 Forbid, 7 BadRequest:

| Situation | Response |
|---|---|
| A missing record on a get endpoint | HTTP 404 with a `status` object ("Record not found!") |
| A request that fails the endpoint's validator or the model binding | HTTP 400 with `validationMessages` |
| A request rejected inside the service | the endpoint's own mapping: HTTP 200, 400 or 500 with `status.code` 3 or 7 and `validationMessages` |
| An unhandled exception | HTTP 500 with `status.code` 3 and a fixed message, no payload |

The protocol reads a 404 with a `status` object as `NotFound`, and every other error shape as `Failed`.

**Decision: a vendor project per service.** **Trade-off:** one more project per service, in exchange for a domain project never depending on a vendor.

## 6. Domain adapters

A domain adapter lives in the owning domain's `*Core`, depends on `HTGPlus.ExternalApi` only, and takes the named service from the host. It builds the request, calls `QueryAsync` or `CommandAsync`, and maps the response to its own types.

| Example | Call |
|---|---|
| Allocate the next agent number | `CommandAsync` (a write; never retried) |
| Read a Payroll option list | `QueryAsync` |

One adapter per domain, one method per call, and an interface only where a test seam is needed.

Each domain `*Core` registers its adapters with one extension method (for example `AddAgentExternalApis()`), so a host adds one line per domain, however many adapters the domain has.

The adapter does not catch `Failed` to hide it. A failure reaches the caller.

## 7. Failure modes

| Failure | What happens | Recovery |
|---|---|---|
| Service down or slow | A query throws `ExternalServiceException` with kind `Transient`; the request fails | The user clicks again; a consumer sends the message back to the queue |
| Command fails transiently | Throws once (repeating a non-idempotent call can run it twice) | The caller or redelivery |
| Token expired | One refresh and one repeat of the request; a second 401 is `Failed` | Automatic |
| Login fails | `Failed` with the status and a truncated body logged | Fix the credentials |
| Validation message in a 200, or a 400 | `Failed` with the messages | Fix the request |
| Host has not registered the service | The call fails and names the service | Register the service in that host |

## 8. Scale

| Question | Answer |
|---|---|
| What limits throughput | the service itself; the transport adds no queue |
| What isolates services | each service has its own named client, token and timeouts |
| What bounds a login | one login in flight per service, shared by every caller |

## 9. Observability

| What | How |
|---|---|
| Service failures | one `[ExternalApi]` log per failed call with service, method, path, kind, status, the error, the payload that was sent and the raw response (each up to 4000 characters; a longer text keeps its first and last 2000 and drops the middle, because Payroll echoes the payload and puts its error at the end), for queries and commands, because the service owner asks for it. A successful call logs nothing |
| Masked | social security numbers, in the payload and in any logged error text, as `XXX-XX-1234`. Nothing else is masked |
| Never logged | credentials and tokens |

## 10. Hosts and configuration

A host registers each service it calls with one method.

| Setting | Meaning |
|---|---|
| `PayrollService` section | `BaseUri`, `Uri`, `Username`, `Password`, `TokenCacheTtlMinutes` (default 50: Payroll's token expires after 60 minutes, so the cache ends before it does) |
| `PayrollService:QueryTimeoutSeconds`, `PayrollService:CommandTimeoutSeconds` | how long one query or one command may take before it fails as transient; default 60 and 120 for Payroll, 30 and 30 for any other service |

A host that never calls a service does not register it and needs none of its settings. DevOps adds the credentials (`Username`, `Password`) to 1Password. The developer adds the section's variables to the deployment pipeline of every host that calls the service.

## 11. Decisions and prerequisites

Prerequisites outside the code: none for Payroll. Its source answers a missing record with a 404 and a `status` object, a rejected request with a 400 or a `validationMessages` list, and a crash with a 500 (section 5).

## 12. Adding a service

Create `HTGPlus.ExternalApi.<Vendor>` with an authenticator, a protocol, its options and one registration method. Register it in each host that needs it. The transport library does not change.

## 13. Tests

| Subject | Pinned |
|---|---|
| Transport | one refresh on a 401, a token per service, no retry for a query or a command, the three outcomes, a validation message inside a 200, a failure logs the sent payload with the SSN masked and a success logs nothing, the failure kind per status, the raw response in the log and not in the error |
| Vendor | the body per call, the dbCode year rule, the envelope read, a service error throws and is not an empty result, `Message` on every outcome, a command with a non-zero code and a message is `Failed` while a query with one is `Ok`, validation messages win over the status message and are joined, a 403 carries the service's text, an SSN in the text is masked |

## 14. Out of scope

N/A. The Summary's scope already excludes the lookup design and the saving screens.
