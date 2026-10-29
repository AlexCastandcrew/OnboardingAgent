namespace OnboardingAgent.ExternalApi.Bedrock;

public sealed class BedrockOptions
{
    public const string Section = "Bedrock";

    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Bedrock API key (bearer token). A secret: keep it in user secrets or the Bedrock__ApiKey environment variable,
    /// never in appsettings.json. When empty, the default AWS credential chain is used.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Model (or inference profile) that writes the final answer. Sonnet 5.5 by plan.</summary>
    public string AnswerModelId { get; set; } = "";

    /// <summary>Model for query rewrite and reranking. Haiku 5.5 by plan.</summary>
    public string AuxModelId { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 2;
    public int AnswerMaxTokens { get; set; } = 2500;
    public int AuxMaxTokens { get; set; } = 600;

    /// <summary>
    /// Sends <c>thinking: disabled</c> to the auxiliary model (rewrite and rerank only). Haiku 5.5 accepts it and then
    /// answers with far fewer output tokens. The answer model rejects that value (it does not think before
    /// responding), so it is never sent there. Turn it off if the auxiliary model id is changed to one that rejects it.
    /// </summary>
    public bool DisableAuxThinking { get; set; }

    /// <summary>Every problem with these settings. Missing model ids are not here: see <see cref="Warnings"/>.</summary>
    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Region))
        {
            yield return $"{Section}:Region is required.";
        }

        foreach (var (name, value) in new[] { (nameof(TimeoutSeconds), TimeoutSeconds), (nameof(AnswerMaxTokens), AnswerMaxTokens), (nameof(AuxMaxTokens), AuxMaxTokens) })
        {
            if (value < 1)
            {
                yield return $"{Section}:{name} ({value}) must be at least 1.";
            }
        }

        if (MaxRetries < 0)
        {
            yield return $"{Section}:MaxRetries ({MaxRetries}) must be 0 or more.";
        }
    }

    /// <summary>The API still starts without model ids (health reports degraded, chat returns 502), so they only warn.</summary>
    public IEnumerable<string> Warnings()
    {
        foreach (var (name, value) in new[] { (nameof(AnswerModelId), AnswerModelId), (nameof(AuxModelId), AuxModelId) })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield return $"{Section}:{name} is not set; /api/chat will return 502 until it is.";
            }
        }
    }
}
