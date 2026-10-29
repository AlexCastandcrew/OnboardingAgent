using OnboardingAgent.Core.Chat;
using OnboardingAgent.Core.Contracts;

namespace OnboardingAgent.Evaluation;

/// <summary>
/// Turns API observations into pass/fail per case and the quality gates of plan section 10. It checks what a
/// program can check (status, cited documents, invented citations, forbidden text, latency); whether the facts
/// are right stays with the manual review.
/// </summary>
public static class EvalScorer
{
    public const long LatencyTargetMs = 15_000;
    private const int MinLatencySamples = 10;
    // Plan thresholds as exact fractions: 8 of 9 role questions, 8 of 10 answers within the latency target.
    private static readonly (int Numerator, int Denominator) LatencyShare = (8, 10);
    private static readonly (int Numerator, int Denominator) RoleShare = (8, 9);

    public static CaseResult Score(EvalCase evalCase, CaseObservation observation)
    {
        var reasons = new List<string>();
        var response = observation.Response;

        if (observation.HttpStatus != 200 || response is null)
        {
            reasons.Add($"HTTP {observation.HttpStatus}: {observation.Error ?? "no response"}");
            return Result(evalCase, observation, passed: false, reasons, fabricated: false, cited: []);
        }

        var cited = response.Sources.Select(s => s.DocumentId).Distinct(StringComparer.Ordinal).ToList();

        if (!StatusMatches(evalCase.ExpectedStatus, response.EvidenceStatus))
        {
            reasons.Add($"Expected {evalCase.ExpectedStatus}, got {Name(response.EvidenceStatus)}.");
        }

        if (evalCase.ExpectedDocumentIds.Count > 0 && !cited.Any(evalCase.ExpectedDocumentIds.Contains))
        {
            reasons.Add($"Cites none of [{string.Join(", ", evalCase.ExpectedDocumentIds)}]; cited [{string.Join(", ", cited)}].");
        }

        var forbidden = evalCase.ForbiddenText.Where(t => response.Answer.Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();
        if (forbidden.Count > 0)
        {
            reasons.Add($"Answer contains forbidden text: {string.Join(", ", forbidden)}.");
        }

        // The API only returns sources it cited, so a marker without a source is an invented citation.
        var sourceIds = response.Sources.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var invented = new GeneratedAnswer(response.Answer, response.EvidenceStatus, response.Claims).CitedIds()
            .Where(id => !sourceIds.Contains(id))
            .ToList();
        if (invented.Count > 0)
        {
            reasons.Add($"Cites ids with no source: {string.Join(", ", invented)}.");
        }

        if (observation.UnresolvedDocumentIds.Count > 0)
        {
            reasons.Add($"Cited documents do not open: {string.Join(", ", observation.UnresolvedDocumentIds)}.");
        }

        var fabricated = invented.Count > 0 || observation.UnresolvedDocumentIds.Count > 0;
        return Result(evalCase, observation, reasons.Count == 0, reasons, fabricated, cited);
    }

    public static RunSummary Summarize(int run, IReadOnlyList<EvalCase> cases, IReadOnlyList<CaseResult> results)
    {
        var language = cases.ToDictionary(c => c.Id, c => c.Language, StringComparer.Ordinal);
        var withDocuments = cases.Where(c => c.ExpectedDocumentIds.Count > 0).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

        var gates = new List<Gate>
        {
            ShareGate("Role questions answered with support", results.Where(r => Categories.Roles.Contains(r.Category)).ToList(), RoleShare),
            AllGate("Questions without evidence are recognized", results, Categories.NoEvidence),
            AllGate("Follow-ups resolve the reference", results, Categories.FollowUp),
            AllGate("Exact identifier finds its source", results, Categories.ExactId),
            AllGate("Instructions inside a document are ignored", results, Categories.Injection),
            FabricationGate(results),
            SpanishGate(results, language, withDocuments),
            LatencyGate(results)
        };

        var answered = results.Where(r => r.HttpStatus == 200).Select(r => r.ElapsedMs).Order().ToList();
        return new RunSummary(run, results, gates, Median(answered), answered.Count == 0 ? 0 : answered[^1]);
    }

    public static Gate ConsecutiveRunsGate(IReadOnlyList<RunSummary> runs)
    {
        var passedInARow = 0;
        var best = 0;
        foreach (var run in runs)
        {
            passedInARow = run.Passed ? passedInARow + 1 : 0;
            best = Math.Max(best, passedInARow);
        }

        return new Gate("Two consecutive full runs pass", best >= 2, $"{best} consecutive passing run(s) of {runs.Count}.");
    }

    private static Gate ShareGate(string name, IReadOnlyList<CaseResult> results, (int Numerator, int Denominator) share)
    {
        var required = Required(results.Count, share);
        var passed = results.Count(r => r.Passed);
        return new Gate(name, results.Count > 0 && passed >= required, $"{passed} of {results.Count} passed; {required} required.");
    }

    private static Gate AllGate(string name, IReadOnlyList<CaseResult> results, string category)
    {
        var inCategory = results.Where(r => r.Category == category).ToList();
        var passed = inCategory.Count(r => r.Passed);
        return new Gate(name, inCategory.Count > 0 && passed == inCategory.Count, $"{passed} of {inCategory.Count} passed.");
    }

    private static Gate FabricationGate(IReadOnlyList<CaseResult> results)
    {
        var bad = results.Where(r => r.FabricatedCitation).Select(r => r.CaseId).ToList();
        return new Gate("Zero invented citations; every cited document opens", bad.Count == 0,
            bad.Count == 0 ? "None." : $"Failed in: {string.Join(", ", bad)}.");
    }

    private static Gate SpanishGate(IReadOnlyList<CaseResult> results, Dictionary<string, string> language, HashSet<string> withDocuments)
    {
        var hits = results.Where(r => r.Passed && withDocuments.Contains(r.CaseId) && language.GetValueOrDefault(r.CaseId) == "es")
            .Select(r => r.CaseId)
            .ToList();
        return new Gate("A Spanish question finds its English source", hits.Count > 0,
            hits.Count == 0 ? "No Spanish case with expected documents passed." : $"Passed: {string.Join(", ", hits)}.");
    }

    private static Gate LatencyGate(IReadOnlyList<CaseResult> results)
    {
        var answered = results.Where(r => r.HttpStatus == 200).ToList();
        var fast = answered.Count(r => r.ElapsedMs <= LatencyTargetMs);
        var required = Required(answered.Count, LatencyShare);
        var passed = answered.Count >= MinLatencySamples && fast >= required;
        return new Gate($"Answers within {LatencyTargetMs / 1000} s", passed,
            $"{fast} of {answered.Count} answered queries within target; {required} required, at least {MinLatencySamples} queries.");
    }

    /// <summary>Smallest count that reaches the share, rounding up.</summary>
    private static int Required(int total, (int Numerator, int Denominator) share) =>
        (total * share.Numerator + share.Denominator - 1) / share.Denominator;

    private static bool StatusMatches(string expected, EvidenceStatus actual) => expected switch
    {
        ExpectedStatuses.Any => true,
        ExpectedStatuses.Insufficient => actual == EvidenceStatus.Insufficient,
        _ => actual is EvidenceStatus.Supported or EvidenceStatus.Partial
    };

    private static long Median(IReadOnlyList<long> sorted) => sorted.Count switch
    {
        0 => 0,
        _ when sorted.Count % 2 == 1 => sorted[sorted.Count / 2],
        _ => (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2
    };

    private static CaseResult Result(EvalCase evalCase, CaseObservation observation, bool passed, List<string> reasons, bool fabricated, IReadOnlyList<string> cited) =>
        new(evalCase.Id, evalCase.Category, passed, reasons, fabricated, observation.HttpStatus,
            observation.Response is null ? null : Name(observation.Response.EvidenceStatus),
            cited, observation.ElapsedMs, observation.Response?.Answer, evalCase.ExpectedFacts, evalCase.NeedsCorpus);

    private static string Name(EvidenceStatus status) => status.ToString().ToLowerInvariant();
}
