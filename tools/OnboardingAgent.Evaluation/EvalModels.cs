using OnboardingAgent.Core.Contracts;

namespace OnboardingAgent.Evaluation;

public static class Categories
{
    public const string Developer = "developer";
    public const string Qa = "qa";
    public const string Leader = "leader";
    public const string FollowUp = "followUp";
    public const string NoEvidence = "noEvidence";
    public const string ExactId = "exactId";
    public const string Injection = "injection";

    public static readonly IReadOnlySet<string> Roles = new HashSet<string> { Developer, Qa, Leader };
}

public static class ExpectedStatuses
{
    /// <summary>supported or partial.</summary>
    public const string Answered = "answered";
    public const string Insufficient = "insufficient";
    public const string Any = "any";
}

public sealed class EvalCaseFile
{
    public List<EvalCase> Cases { get; set; } = [];
}

public sealed class EvalCase
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string ProjectId { get; set; } = "htg";
    public string Role { get; set; } = "";
    public string? Domain { get; set; }
    public string? Initiative { get; set; }
    public string Question { get; set; } = "";

    /// <summary>Language of the question; the corpus is in English.</summary>
    public string Language { get; set; } = "es";

    public List<ChatHistoryItem> History { get; set; } = [];
    public string ExpectedStatus { get; set; } = ExpectedStatuses.Answered;

    /// <summary>The answer must cite at least one of these. Empty means no document check.</summary>
    public List<string> ExpectedDocumentIds { get; set; } = [];

    /// <summary>Text that must never appear in the answer (prompt-injection markers).</summary>
    public List<string> ForbiddenText { get; set; } = [];

    /// <summary>For the manual review only; the runner does not judge facts.</summary>
    public List<string> ExpectedFacts { get; set; } = [];

    /// <summary>True when the expected documents are not in the corpus yet (T03).</summary>
    public bool NeedsCorpus { get; set; }
}

/// <summary>What one call to the API returned, before scoring.</summary>
public sealed record CaseObservation(
    int HttpStatus,
    ChatResponse? Response,
    string? Error,
    long ElapsedMs,
    IReadOnlyList<string> UnresolvedDocumentIds);

public sealed record CaseResult(
    string CaseId,
    string Category,
    bool Passed,
    IReadOnlyList<string> Reasons,
    bool FabricatedCitation,
    int HttpStatus,
    string? EvidenceStatus,
    IReadOnlyList<string> CitedDocumentIds,
    long ElapsedMs,
    string? Answer,
    IReadOnlyList<string> ExpectedFacts,
    bool NeedsCorpus);

public sealed record Gate(string Name, bool Passed, string Detail);

public sealed record RunSummary(int Run, IReadOnlyList<CaseResult> Cases, IReadOnlyList<Gate> Gates, long MedianMs, long MaxMs)
{
    public bool Passed => Gates.All(g => g.Passed);
}

public sealed record EvalReport(DateTimeOffset StartedAt, string BaseUrl, IReadOnlyList<RunSummary> Runs, Gate ConsecutiveRuns);
