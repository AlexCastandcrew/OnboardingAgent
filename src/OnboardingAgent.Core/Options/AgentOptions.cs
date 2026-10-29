namespace OnboardingAgent.Core.Options;

public sealed class CorpusOptions
{
    public const string Section = "Corpus";

    /// <summary>Folder with the corpus Markdown files. Relative paths resolve against the content root.</summary>
    public string Folder { get; set; } = "../../corpus";

    public string ManifestFile { get; set; } = "manifest.json";

    /// <summary>Folder with projects.json and onboarding.json.</summary>
    public string DataFolder { get; set; } = "../../data";

    /// <summary>Folder for generated state: the SQLite index, feedback and knowledge gaps.</summary>
    public string StateFolder { get; set; } = "../../.data";

    public bool IngestOnStartup { get; set; } = true;

    /// <summary>About 500 tokens at ~4 characters per token.</summary>
    public int ChunkTargetChars { get; set; } = 2000;

    /// <summary>About 80 tokens.</summary>
    public int ChunkOverlapChars { get; set; } = 320;

    /// <summary>File name patterns the ingestion refuses, even if listed in the manifest.</summary>
    public List<string> ExcludedPatterns { get; set; } = [];

    /// <summary>Every problem with these settings, as messages that name the setting.</summary>
    public IEnumerable<string> Validate()
    {
        foreach (var (name, value) in new[] { (nameof(Folder), Folder), (nameof(ManifestFile), ManifestFile), (nameof(DataFolder), DataFolder), (nameof(StateFolder), StateFolder) })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield return $"{Section}:{name} is required.";
            }
        }

        if (ChunkTargetChars < 200)
        {
            yield return $"{Section}:ChunkTargetChars ({ChunkTargetChars}) must be at least 200.";
        }

        if (ChunkOverlapChars < 0 || ChunkOverlapChars >= ChunkTargetChars)
        {
            yield return $"{Section}:ChunkOverlapChars ({ChunkOverlapChars}) must be 0 or more and smaller than ChunkTargetChars ({ChunkTargetChars}).";
        }
    }
}

public sealed class ChatOptions
{
    public const string Section = "Chat";

    public int MaxMessageChars { get; set; } = 2000;
    public int MaxHistoryItems { get; set; } = 6;
    public int MaxHistoryItemChars { get; set; } = 2000;

    /// <summary>BM25 candidates handed to the reranker.</summary>
    public int CandidateCount { get; set; } = 20;

    /// <summary>Passages handed to the answer model (plan: 4 to 6).</summary>
    public int ContextChunks { get; set; } = 6;

    /// <summary>Candidates one document may take, so a long document cannot crowd out the rest.</summary>
    public int MaxChunksPerDocument { get; set; } = 4;

    /// <summary>Relevance floor on the boosted BM25 score. 0 keeps every match; calibrate with the evaluation.</summary>
    public double MinScore { get; set; }

    public double InitiativeBoost { get; set; } = 1.5;
    public double DomainBoost { get; set; } = 1.25;

    public int RequestTimeoutSeconds { get; set; } = 45;

    /// <summary>Every problem with these settings, as messages that name the setting.</summary>
    public IEnumerable<string> Validate()
    {
        foreach (var (name, value) in new[] { (nameof(MaxMessageChars), MaxMessageChars), (nameof(MaxHistoryItemChars), MaxHistoryItemChars), (nameof(CandidateCount), CandidateCount), (nameof(RequestTimeoutSeconds), RequestTimeoutSeconds) })
        {
            if (value < 1)
            {
                yield return $"{Section}:{name} ({value}) must be at least 1.";
            }
        }

        if (MaxHistoryItems < 0)
        {
            yield return $"{Section}:MaxHistoryItems ({MaxHistoryItems}) must be 0 or more.";
        }

        if (ContextChunks < 1 || ContextChunks > CandidateCount)
        {
            yield return $"{Section}:ContextChunks ({ContextChunks}) must be between 1 and CandidateCount ({CandidateCount}).";
        }

        if (MaxChunksPerDocument < 1 || MaxChunksPerDocument > CandidateCount)
        {
            yield return $"{Section}:MaxChunksPerDocument ({MaxChunksPerDocument}) must be between 1 and CandidateCount ({CandidateCount}).";
        }

        if (MinScore < 0)
        {
            yield return $"{Section}:MinScore ({MinScore}) must be 0 or more.";
        }

        if (InitiativeBoost < 1 || DomainBoost < 1)
        {
            yield return $"{Section}:InitiativeBoost ({InitiativeBoost}) and DomainBoost ({DomainBoost}) must be 1 or more; a boost below 1 would push the chosen team's documents down.";
        }
    }
}
