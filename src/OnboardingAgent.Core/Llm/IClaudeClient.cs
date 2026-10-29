using System.Text.Json;

namespace OnboardingAgent.Core.Llm;

public enum ModelTier
{
    /// <summary>Final answer (Sonnet).</summary>
    Answer,

    /// <summary>Query rewrite and reranking (Haiku).</summary>
    Aux
}

/// <param name="Role">"user" or "assistant".</param>
public sealed record ClaudeMessage(string Role, string Text);

/// <summary>One call whose reply must be JSON matching <paramref name="SchemaJson"/> (structured output).</summary>
/// <param name="SchemaName">Short name of the schema, also used in logs.</param>
public sealed record ClaudeStructuredCall(
    ModelTier Tier,
    string System,
    IReadOnlyList<ClaudeMessage> Messages,
    string SchemaName,
    string SchemaDescription,
    string SchemaJson);

/// <summary>Claude, wherever it is hosted. The Bedrock implementation lives in OnboardingAgent.ExternalApi.Bedrock.</summary>
public interface IClaudeClient
{
    /// <summary>Which model ids answer each tier, for the health endpoint. Ids are configuration, not secrets.</summary>
    ModelInfo Models { get; }

    Task<JsonElement> InvokeStructuredAsync(ClaudeStructuredCall call, CancellationToken cancellationToken);
}

/// <param name="Auth">How the client authenticates (for example apiKey or awsCredentials). Never the secret itself.</param>
public sealed record ModelInfo(string Provider, string Region, string AnswerModelId, string AuxModelId, string Auth)
{
    public bool Configured => !string.IsNullOrWhiteSpace(AnswerModelId) && !string.IsNullOrWhiteSpace(AuxModelId);
}

/// <summary>The model call failed or returned something unusable. Maps to 502, never to "insufficient evidence".</summary>
public sealed class LlmException(string message, Exception? inner = null) : Exception(message, inner);
