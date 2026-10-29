# Traditional extraction inside htg-server — Design (direct in-process extractor)

Scope: reading the timecards of a Traditional-template worksheet, and listing the worksheets of an uploaded file, inside the
Excel Mapping pods instead of calling the DataIngestion service over HTTP. The AI (DocAI) and Unstructured paths are unchanged.

---

## Summary

When a payroll spreadsheet is uploaded, Excel Mapping reads its rows into timecards. For Traditional templates it does this by
sending the file to a separate service (DataIngestion) and waiting for the answer. The same reading code already lives inside
htg-server. This design uses it and retires the call.

**Why.** Every Traditional file depends on a second service being up, reachable and fast. If it is down, the file fails. The team
keeps a copy of that service's reading code in htg-server, but the pods do not use it, so the service cannot be switched off.

**The design**
- **One in-process extractor.** A single component in the Excel Mapping project turns a template into the reader's template,
  reads the file, and returns the timecards in the shape the mappers already consume.
- **Both pipelines use it.** The new pods (template decider, extract processor) and the old subscriber path call the same
  extractor, so a file produces the same timecards whichever path processes it.
- **Worksheet listing moves too.** The list of worksheets in a file is read by the same reader, so no DataIngestion call remains.
- **Same results as today.** The output matches what the service returns for the same file and template, including its
  handling of template fields the service ignores.
- **The HTTP client goes.** The DataIngestion client, its models, its configuration and its authentication handler are removed.

**What it gives the business.** Traditional files no longer depend on a second service. The DataIngestion service can be
switched off.

**What we need from others.** The DataIngestion service keeps running until the release is verified in QA, then the owning team
switches it off and removes its secrets and configuration. Pod memory limits are checked, because parsing now happens inside the
Excel Mapping pods.

**Risks we accept.** A very large or malformed file now uses memory and CPU in the Excel Mapping pod instead of the other
service; the pod can run out of memory on a file the service could take. The file fails and shows as an error on the dashboard.
There is no switch to go back to the service; rollback is redeploying the previous version while the service is still running.

**Scope.** Traditional timecard extraction and worksheet listing, in the new pods and the old subscriber path.

---

## 1. Problem

| Behavior | Consequence |
|---|---|
| The Traditional extraction strategy and the old extraction service post the file, the template and the metadata to the DataIngestion service | A Traditional file cannot be processed when that service is unavailable. |
| The worksheet listing (extract provider, old extraction service) posts the file to the same service | The listing fails the same way; the file is treated as having no worksheets. |
| The in-process reader exists in `HTGPlus.ExcelMapping.Extraction` and the API uses it for column discovery; the Subscriber has no reference to it | The service stays in the runtime path and cannot be retired. |

## 2. Goals and non-goals

Goals
- A Traditional worksheet produces the same timecards in-process as it does through the service today.
- The new pods and the old subscriber path share one extraction implementation.
- No call to the DataIngestion service remains, so it can be retired.

Non-goals
- Changing what the mappers do with the timecards, the message to the mapper, or the AI and Unstructured paths.
- Changing the reader's parsing rules.
- Retiring the service itself (the owning team does it).

## 3. Architecture

```
extract-processor ─┐
template-decider ──┼─► TraditionalExtractor ──► IFileExtractionService (HTGPlus.ExcelMapping.Extraction)
old subscriber ────┘          │
                              └─► DataExtractionResponse (timecards) ──► existing mappers
```

- **TraditionalExtractor:** the only place that converts a template and metadata into the reader's input, runs the reader, and
  converts its output to the mapper's response.
- **IFileExtractionService:** reads Excel, XLS and CSV files and lists worksheets; unchanged.
- **Callers:** the extraction strategy, the old extraction service and the three worksheet-listing sites ask the extractor; they
  keep their own tracking and failure handling.

## 4. Components

### 4.1 TraditionalExtractor

- Input: file bytes, file name, the matched template, the request metadata. Output: the same list of worksheet responses the
  mappers consume today, or an error result.
- Converts the matched template straight into the reader's template, with no intermediate HTTP-shaped model. A mapping field whose
  expression type the reader does not know (set-field, concat) becomes the plain type, as the service does when it reads such a
  field; the field is not dropped.
- Stamps the file name, EOR id, parent client id and client id from the metadata on each response, the only metadata the service
  uses. Missing ids become empty strings.
- Converts the reader's `Timecard` to the mapper's `TimecardItem` field by field. The reader holds gross amounts as `double`,
  the mapper as `decimal`; the conversion goes through the value's shortest round-trip text, so `0.1` stays `0.1`.
- A parser exception (unsupported or corrupt file) becomes an error result with a fixed message, "Unable to extract data from
  excel"; the exception itself is logged once with the file key, file name, worksheet and template id, so the dashboard never
  shows parser text. A cancellation is not caught. The extractor is the only place in this path that catches; callers act on the
  result as they do on a failed service call.
- Does not branch on caller: both pipelines pass data, not flags.

Trade-offs: one extractor shared by both pipelines means a change to it changes both. Cost: the old subscriber path, being
retired, gets the new behavior too.

### 4.2 Worksheet listing

The extractor also lists worksheet names, from the reader's header read. The three listing sites call it. An unreadable file
returns an empty list and a logged error with the file key, as today.

### 4.3 Removal

`IDataIngestionService`, its HTTP implementation, request and response models, the HTTP template types, `DataIngestionConfig`,
the authentication handler and `AddDataIngestion` are removed, and so is the `DataIngestionIntegration` section of the shared
configuration. Every pod that registered the client registers the reader and the extractor instead. The mapping pipeline reads
the matched template's mappings directly, so it no longer needs a template copy. Registration is removed only after every
consumer, including tests, is moved.

## 5. Failure modes

| Failure | What happens | Recovery |
|---|---|---|
| Corrupt or unsupported file | Error result; the file or worksheet is tracked as an error with the reason | Fix and upload again |
| Pod out of memory on a very large file | Pod restarts, offset not stored, message redelivered; repeated crashes | Raise the memory limit; the file is the cause |
| Reader returns zero timecards | Mapper receives an empty list; logged as today | None; the template does not match the data |
| Template field with an expression type the reader does not know | Treated as the plain type | None; same as the service |

## 6. Scale

Parsing uses the CPU and memory of the pod that consumes the message, one message at a time per consumer instance. Throughput
grows with partitions and pods. The service's own capacity no longer limits it.

## 7. Observability

- Log prefix and scope as the strategy has today: file key, file name, worksheet, template id. Template and metadata objects are
  logged as today; no timecard values (SSN, tax id) are logged.
- The "No timecards" information line the service wrote moves to the extractor, with file key and worksheet.
- Verification after deploy: in QA, for the same sample files, the timecard count per worksheet equals the count the service
  returned; no DataIngestion request appears in the pod logs or the service's access log.

## 8. Decisions and prerequisites

No open decisions. Prerequisites outside the code: the service stays up until QA verification, then the owning team retires it
and removes its secrets and configuration; pod memory limits for the extract processor and template decider are reviewed.

## 9. Out of scope

- Retiring the service, its infrastructure and its repository.
- Parsing rule changes in the reader.
- AI and Unstructured paths.
