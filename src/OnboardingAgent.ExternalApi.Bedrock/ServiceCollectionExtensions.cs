using Microsoft.Extensions.DependencyInjection;
using OnboardingAgent.Core.Llm;

namespace OnboardingAgent.ExternalApi.Bedrock;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers Claude on AWS Bedrock as the <see cref="IClaudeClient"/>.</summary>
    public static IServiceCollection AddBedrockClaude(this IServiceCollection services, BedrockOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<IClaudeClient, BedrockClaudeClient>();
        return services;
    }
}
