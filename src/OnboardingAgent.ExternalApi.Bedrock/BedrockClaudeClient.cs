using System.Text.Json;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Logging;
using OnboardingAgent.Core.Llm;

namespace OnboardingAgent.ExternalApi.Bedrock;

/// <summary>
/// Claude on AWS Bedrock through the Converse API. Authenticates with the Bedrock API key when one is configured,
/// otherwise with the default AWS credential chain (profile, SSO, environment).
/// </summary>
public sealed class BedrockClaudeClient : IClaudeClient, IDisposable
{
    private readonly AmazonBedrockRuntimeClient _client;
    private readonly BedrockOptions _options;
    private readonly ILogger<BedrockClaudeClient> _logger;

    public BedrockClaudeClient(BedrockOptions options, ILogger<BedrockClaudeClient> logger)
    {
        _options = options;
        _logger = logger;
        var config = new AmazonBedrockRuntimeConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
            MaxErrorRetry = options.MaxRetries
        };

        var usesApiKey = !string.IsNullOrWhiteSpace(options.ApiKey);
        if (usesApiKey)
        {
            config.AWSTokenProvider = new ServiceBearerStaticTokenProvider(options.ApiKey!.Trim(), null);
        }

        Models = new ModelInfo("bedrock", options.Region, options.AnswerModelId, options.AuxModelId, usesApiKey ? "apiKey" : "awsCredentials");
        _client = new AmazonBedrockRuntimeClient(config);
    }

    public ModelInfo Models { get; }

    public async Task<JsonElement> InvokeStructuredAsync(ClaudeStructuredCall call, CancellationToken cancellationToken)
    {
        var (modelId, maxTokens) = call.Tier == ModelTier.Answer
            ? (_options.AnswerModelId, _options.AnswerMaxTokens)
            : (_options.AuxModelId, _options.AuxMaxTokens);
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new LlmException($"No Bedrock model id is configured for the {call.Tier} tier (Bedrock:{call.Tier}ModelId).");
        }

        // Structured output (a JSON schema for the reply) instead of a forced tool call, which current Claude models
        // reject. No temperature either: current models reject it.
        var request = new ConverseRequest
        {
            ModelId = modelId,
            System = [new SystemContentBlock { Text = call.System }],
            Messages = call.Messages.Select(m => new Message
            {
                Role = m.Role == "assistant" ? ConversationRole.Assistant : ConversationRole.User,
                Content = [new ContentBlock { Text = m.Text }]
            }).ToList(),
            InferenceConfig = new InferenceConfiguration { MaxTokens = maxTokens },
            OutputConfig = new OutputConfig
            {
                TextFormat = new OutputFormat
                {
                    Type = OutputFormatType.Json_schema,
                    Structure = new OutputFormatStructure
                    {
                        JsonSchema = new JsonSchemaDefinition
                        {
                            Name = call.SchemaName,
                            Description = call.SchemaDescription,
                            Schema = call.SchemaJson
                        }
                    }
                }
            }
        };

        if (call.Tier == ModelTier.Aux && _options.DisableAuxThinking)
        {
            request.AdditionalModelRequestFields = new Amazon.Runtime.Documents.Document(new Dictionary<string, Amazon.Runtime.Documents.Document>
            {
                ["thinking"] = new Amazon.Runtime.Documents.Document(new Dictionary<string, Amazon.Runtime.Documents.Document>
                {
                    ["type"] = "disabled"
                })
            });
        }

        ConverseResponse response;
        var started = TimeProvider.System.GetTimestamp();
        try
        {
            response = await _client.ConverseAsync(request, cancellationToken);
        }
        catch (AmazonServiceException ex)
        {
            _logger.LogError(ex, "[Bedrock] Converse failed for model {ModelId} tier {Tier} schema {Schema}: {Status} {ErrorCode}",
                modelId, call.Tier, call.SchemaName, (int)ex.StatusCode, ex.ErrorCode);
            throw new LlmException($"The model service returned an error ({ex.ErrorCode ?? ex.StatusCode.ToString()}).", ex);
        }
        catch (AmazonClientException ex)
        {
            _logger.LogError(ex, "[Bedrock] Converse could not reach model {ModelId} tier {Tier} schema {Schema}", modelId, call.Tier, call.SchemaName);
            throw new LlmException("The model service could not be reached. Check the API key or AWS credentials and the region.", ex);
        }

        _logger.LogInformation("[Bedrock] {Tier} {Schema} took {ElapsedMs} ms, tokens in {In} out {Out}, stop {Stop}",
            call.Tier, call.SchemaName, (long)TimeProvider.System.GetElapsedTime(started).TotalMilliseconds,
            response.Usage?.InputTokens, response.Usage?.OutputTokens, response.StopReason?.Value);

        if (response.StopReason == StopReason.Max_tokens)
        {
            _logger.LogWarning("[Bedrock] {Tier} model {ModelId} hit max tokens ({MaxTokens}) on {Schema}, input {In} tokens",
                call.Tier, modelId, maxTokens, call.SchemaName, response.Usage?.InputTokens);
            throw new LlmException($"The answer was cut off at the {call.Tier} model's limit of {maxTokens} tokens.");
        }

        var text = string.Concat(response.Output?.Message?.Content?.Select(c => c.Text) ?? []);
        try
        {
            using var json = JsonDocument.Parse(text);
            return json.RootElement.Clone();
        }
        catch (JsonException)
        {
            _logger.LogWarning("[Bedrock] {Tier} model {ModelId} returned {Length} characters that are not {Schema} JSON, stop reason {Stop}",
                call.Tier, modelId, text.Length, call.SchemaName, response.StopReason?.Value);
            throw new LlmException("The model did not return a structured answer.");
        }
    }

    public void Dispose() => _client.Dispose();
}
