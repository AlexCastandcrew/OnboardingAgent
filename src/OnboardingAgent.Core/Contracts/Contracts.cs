using System.ComponentModel.DataAnnotations;

namespace OnboardingAgent.Core.Contracts;

public static class Roles
{
    public const string Developer = "developer";
    public const string Qa = "qa";
    public const string Leader = "leader";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Developer, Qa, Leader };
}

public static class Languages
{
    public const string Spanish = "es";
    public const string English = "en";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Spanish, English };

    /// <summary>Lowercase and trimmed, or null when empty.</summary>
    public static string? Normalize(string? language) =>
        string.IsNullOrWhiteSpace(language) ? null : language.Trim().ToLowerInvariant();
}

public enum EvidenceStatus
{
    Supported,
    Partial,
    Insufficient
}

// ---------- Chat ----------

public sealed record ChatHistoryItem
{
    /// <summary>"user" or "assistant".</summary>
    [Required] public required string Role { get; init; }
    [Required] public required string Content { get; init; }
}

public sealed record ChatRequest
{
    /// <example>htg</example>
    [Required] public required string ProjectId { get; init; }

    /// <summary>Team inside the project. Optional; omit for "General".</summary>
    /// <example>silverwing</example>
    public string? Domain { get; init; }

    /// <summary>Jira epic key of the team's initiative. Optional; requires domain.</summary>
    /// <example>H2G-6893</example>
    public string? Initiative { get; init; }

    /// <summary>developer, qa or leader.</summary>
    /// <example>developer</example>
    [Required] public required string Role { get; init; }

    /// <summary>Language of the answer: "es" or "en". Optional; when omitted the answer follows the question's language.</summary>
    /// <example>es</example>
    public string? Language { get; init; }

    /// <example>¿Qué hace el import de Excel de Executive Officer Payroll?</example>
    [Required] public required string Message { get; init; }

    /// <summary>Short previous conversation, oldest first. Treated as untrusted input.</summary>
    public List<ChatHistoryItem>? History { get; init; }
}

public sealed record ChatSource(
    string Id,
    string DocumentId,
    string ChunkId,
    string Title,
    string? Url,
    string Section,
    string Excerpt,
    string SourceType,
    string? SourceVersion,
    string? SourceUpdatedAt);

public sealed record ChatClaim(string Text, IReadOnlyList<string> SourceIds);

public sealed record ChatDiagnostics(
    string SearchQuery,
    int CandidateCount,
    int ContextCount,
    bool Reranked,
    int GenerationAttempts,
    long ElapsedMs);

public sealed record ChatResponse(
    Guid AnswerId,
    string Answer,
    EvidenceStatus EvidenceStatus,
    IReadOnlyList<ChatSource> Sources,
    IReadOnlyList<ChatClaim> Claims,
    ChatDiagnostics Diagnostics);

// ---------- Chat activity (what the agent is doing while it answers) ----------

public enum ActivityStatus
{
    /// <summary>Known to come next, not started.</summary>
    Pending,
    Running,
    Completed,
    Failed
}

/// <summary>How many indexed documents and passages of one source type (confluence, jira, repo, manual...) a step used.</summary>
public sealed record SourceTypeCount(string SourceType, int Documents, int Passages);

/// <summary>Numbers behind a step. Only the fields that apply are set. No prompts, queries or passage text.</summary>
public sealed record ActivityDetail(
    int? Candidates = null,
    int? Kept = null,
    int? Attempt = null,
    int? Cited = null,
    bool? Fallback = null,
    IReadOnlyList<SourceTypeCount>? Sources = null);

/// <summary>
/// One step of the chat pipeline. The same <see cref="Id"/> is sent again as its status changes (pending, running, then
/// completed or failed), so a client keeps one row per id. <see cref="Message"/> is English; clients may localize from
/// <see cref="Step"/>.
/// </summary>
public sealed record ChatActivity(
    string Id,
    string Step,
    ActivityStatus Status,
    string Message,
    DateTimeOffset Timestamp,
    ActivityDetail? Detail = null);

/// <summary>Terminal failure of a stream that had already started, so it cannot use an HTTP status.</summary>
public sealed record ChatStreamError(int Status, string Title, string? Detail);

// ---------- Catalog ----------

public sealed record ProjectSummary(string ProjectId, string Name, string Description, string? RepoUrl, string SnapshotDate);

public sealed record InitiativeSummary(string InitiativeId, string Name);

/// <param name="Depth">"full" when the corpus covers the team in depth, "overview" when only the team overview exists.</param>
public sealed record DomainSummary(string DomainId, string Name, string Depth, IReadOnlyList<InitiativeSummary> Initiatives);

// ---------- Knowledge (what is indexed for a project) ----------

public sealed record SourceTypeSummary(string SourceType, int Documents, int Passages);

/// <summary>A Git repository the indexed repo documents come from, taken from their pinned GitHub URLs.</summary>
public sealed record RepositorySummary(string Name, string Url, int Documents, string? Version);

/// <summary>Counts of what the index really holds for a project. Nothing here is configured by hand.</summary>
public sealed record KnowledgeSummary(
    string ProjectId,
    int Documents,
    int Passages,
    string? LastIndexedAt,
    IReadOnlyList<SourceTypeSummary> SourceTypes,
    IReadOnlyList<RepositorySummary> Repositories,
    int Teams,
    int Initiatives);

// ---------- Onboarding ----------

public sealed record OnboardingDocument(string DocumentId, string Title, string SourceType, string? Url);

public sealed record OnboardingLink(string Label, string Url);

/// <param name="Status">"ready" when at least one corpus document backs the step, "pending" otherwise.</param>
public sealed record OnboardingStep(
    int Step,
    string Key,
    string Title,
    string Goal,
    string Status,
    IReadOnlyList<string> DocumentIds,
    IReadOnlyList<OnboardingDocument> Documents,
    IReadOnlyList<OnboardingLink> Links,
    IReadOnlyList<string> SuggestedQuestions);

public sealed record OnboardingRoute(
    string ProjectId,
    string Role,
    string? Domain,
    string? Initiative,
    IReadOnlyList<OnboardingStep> Steps);

// ---------- Documents ----------

public sealed record DocumentSection(string ChunkId, string Section);

public sealed record DocumentView(
    string DocumentId,
    string ProjectId,
    string Title,
    string SourceType,
    string? Url,
    string Path,
    string? SourceVersion,
    string? SourceUpdatedAt,
    string IngestedAt,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> Initiatives,
    string Content,
    IReadOnlyList<DocumentSection> Sections);

// ---------- Ingestion ----------

public sealed record IngestionFailure(string? DocumentId, string Reason);

public sealed record IngestionResult(
    IReadOnlyList<string> Processed,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Removed,
    IReadOnlyList<IngestionFailure> Failed,
    int ChunkCount);

// ---------- Feedback and gaps ----------

public sealed record FeedbackRequest
{
    [Required] public required Guid AnswerId { get; init; }
    [Required] public required bool Helpful { get; init; }

    /// <summary>Optional, at most 500 characters.</summary>
    public string? Comment { get; init; }
}

public sealed record FeedbackRecord(Guid AnswerId, bool Helpful, string? Comment, DateTimeOffset At);

public sealed record KnowledgeGap(
    Guid AnswerId,
    string ProjectId,
    string Role,
    string? Domain,
    string? Initiative,
    string Question,
    DateTimeOffset At);

// ---------- Progress and admin ----------

public sealed record MeResponse(string UserLabel, bool IsAdmin);

public sealed record SaveProgressRequest
{
    /// <example>htg</example>
    [Required] public required string ProjectId { get; init; }

    /// <example>developer</example>
    [Required] public required string Role { get; init; }

    public string? Domain { get; init; }
    public string? Initiative { get; init; }

    /// <summary>Keys of the route steps the user marked as done.</summary>
    public List<string>? DoneSteps { get; init; }

    /// <summary>Steps of the route that have documents, the ones that can be done.</summary>
    public int TotalSteps { get; init; }
}

/// <param name="Level">1 to 4, from the share of the route done.</param>
/// <param name="Day">Day of the onboarding, the first day is 1.</param>
/// <param name="QuestionsAsked">A count. The questions themselves are never stored.</param>
/// <param name="Pace">"ahead", "onPace" or "behind" against the target days.</param>
public sealed record MyProgress(string UserLabel, int Level, int StepsDone, int StepsTotal, int Day, int QuestionsAsked, string Pace, int TargetDays, DateTimeOffset? LastActivityAt);

/// <param name="Sample">True for made-up demo people, never for a real user.</param>
public sealed record PersonProgress(string Person, string Team, string Role, int Level, int StepsDone, int StepsTotal, int Day, string Pace, DateTimeOffset LastActivityAt, bool Sample);

public sealed record AdminOverview(IReadOnlyList<PersonProgress> People, int TargetDays);

// ---------- Health ----------

public sealed record IndexHealth(int Documents, int Chunks, string? LastIngestedAt);

/// <param name="Auth">apiKey or awsCredentials. The key itself is never returned.</param>
public sealed record ModelHealth(string Region, string AnswerModelId, string AuxModelId, string Auth, bool Configured);

public sealed record HealthResponse(string Status, IndexHealth Index, ModelHealth Models);
