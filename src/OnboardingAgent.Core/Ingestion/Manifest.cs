namespace OnboardingAgent.Core.Ingestion;

public sealed class CorpusManifest
{
    public string ProjectId { get; set; } = "";
    public List<ManifestDocument> Documents { get; set; } = [];
}

public sealed class ManifestDocument
{
    public string DocumentId { get; set; } = "";

    /// <summary>Project this document belongs to. Empty means the manifest's own projectId.</summary>
    public string? ProjectId { get; set; }

    /// <summary>Path relative to the corpus folder.</summary>
    public string Path { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>repo, confluence, jira, manual or synthetic.</summary>
    public string SourceType { get; set; } = "";

    public string? SourceUrl { get; set; }

    /// <summary>Commit, page version or snapshot label.</summary>
    public string? SourceVersion { get; set; }

    /// <summary>Date the source was last changed, if known. Not the ingestion date.</summary>
    public string? SourceUpdatedAt { get; set; }

    /// <summary>Team ids, or "general" when the document applies to the whole project.</summary>
    public List<string> Domains { get; set; } = ["general"];

    /// <summary>Jira epic keys; empty when the document applies to the whole domain.</summary>
    public List<string> Initiatives { get; set; } = [];
}

public static class SourceTypes
{
    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { "repo", "confluence", "jira", "manual", "synthetic" };
}
