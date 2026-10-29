using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OnboardingAgent.Core.Contracts;
using OnboardingAgent.Evaluation;

// Runs the evaluation cases against a running API and writes JSON and Markdown results.
//   dotnet run --project tools/OnboardingAgent.Evaluation -- [--base-url http://localhost:5006] [--cases eval/cases.json]
//                                                           [--out eval/results] [--runs 2] [--only id1,id2]
// The bearer token comes from the ONBOARDING_EVAL_TOKEN environment variable, never from the command line.

var options = ParseArgs(args);
var baseUrl = options.GetValueOrDefault("base-url", "http://localhost:5006").TrimEnd('/');
var casesPath = options.GetValueOrDefault("cases", Path.Combine("eval", "cases.json"));
var outFolder = options.GetValueOrDefault("out", Path.Combine("eval", "results"));
var runs = int.Parse(options.GetValueOrDefault("runs", "2"));
var only = options.TryGetValue("only", out var ids) ? ids.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet() : null;

var token = Environment.GetEnvironmentVariable("ONBOARDING_EVAL_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("Set ONBOARDING_EVAL_TOKEN to a bearer token for the API (dotnet user-jwts print <id> --output token).");
    return 2;
}

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

var cases = (JsonSerializer.Deserialize<EvalCaseFile>(File.ReadAllText(casesPath), json)?.Cases ?? [])
    .Where(c => only is null || only.Contains(c.Id))
    .ToList();

using var http = new HttpClient { BaseAddress = new Uri(baseUrl + "/"), Timeout = TimeSpan.FromSeconds(90) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

var started = DateTimeOffset.UtcNow;
var summaries = new List<RunSummary>();
for (var run = 1; run <= runs; run++)
{
    var results = new List<CaseResult>();
    foreach (var evalCase in cases)
    {
        var result = EvalScorer.Score(evalCase, await ObserveAsync(evalCase));
        results.Add(result);
        Console.WriteLine($"run {run} {evalCase.Id,-12} {(result.Passed ? "PASS" : "FAIL")} {result.ElapsedMs,6} ms {string.Join(" ", result.Reasons)}");
    }

    summaries.Add(EvalScorer.Summarize(run, cases, results));
}

var report = new EvalReport(started, baseUrl, summaries, EvalScorer.ConsecutiveRunsGate(summaries));
Directory.CreateDirectory(outFolder);
var stem = Path.Combine(outFolder, started.ToString("yyyyMMdd-HHmmss"));
File.WriteAllText(stem + ".json", JsonSerializer.Serialize(report, json));
File.WriteAllText(stem + ".md", ReportWriter.Markdown(report));

foreach (var gate in summaries[^1].Gates.Append(report.ConsecutiveRuns))
{
    Console.WriteLine($"{(gate.Passed ? "PASS" : "FAIL")}  {gate.Name}: {gate.Detail}");
}

Console.WriteLine($"Report: {Path.GetFullPath(stem + ".md")}");
return summaries.All(s => s.Passed) && report.ConsecutiveRuns.Passed ? 0 : 1;

async Task<CaseObservation> ObserveAsync(EvalCase evalCase)
{
    var request = new ChatRequest
    {
        ProjectId = evalCase.ProjectId,
        Domain = evalCase.Domain,
        Initiative = evalCase.Initiative,
        Role = evalCase.Role,
        Message = evalCase.Question,
        History = evalCase.History
    };

    var clock = Stopwatch.StartNew();
    try
    {
        using var response = await http.PostAsJsonAsync("api/chat", request, json);
        var elapsed = clock.ElapsedMilliseconds;
        if (!response.IsSuccessStatusCode)
        {
            return new CaseObservation((int)response.StatusCode, null, await response.Content.ReadAsStringAsync(), elapsed, []);
        }

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(json);
        var unresolved = new List<string>();
        foreach (var documentId in body!.Sources.Select(s => s.DocumentId).Distinct())
        {
            using var document = await http.GetAsync($"api/documents/{Uri.EscapeDataString(documentId)}");
            if (document.StatusCode != HttpStatusCode.OK)
            {
                unresolved.Add(documentId);
            }
        }

        return new CaseObservation(200, body, null, elapsed, unresolved);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        return new CaseObservation(0, null, ex.Message, clock.ElapsedMilliseconds, []);
    }
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i + 1 < args.Length; i += 2)
    {
        result[args[i].TrimStart('-')] = args[i + 1];
    }

    return result;
}
