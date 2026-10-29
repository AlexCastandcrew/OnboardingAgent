using Microsoft.Extensions.DependencyInjection;
using OnboardingAgent.Core.Catalog;
using OnboardingAgent.Core.Chat;
using OnboardingAgent.Core.Contracts;
using OnboardingAgent.Core.Ingestion;
using OnboardingAgent.Core.Options;
using OnboardingAgent.Core.Retrieval;

namespace OnboardingAgent.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers ingestion, retrieval, the catalog, onboarding routes and the chat pipeline. The host must also
    /// register an <see cref="Llm.IClaudeClient"/> (for example AddBedrockClaude).
    /// </summary>
    public static IServiceCollection AddOnboardingCore(this IServiceCollection services, CorpusOptions corpus, ChatOptions chat, CorpusPaths paths)
    {
        services.AddSingleton(corpus);
        services.AddSingleton(chat);
        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new KnowledgeIndex(Path.Combine(paths.StateFolder, "index.db")));
        services.AddSingleton<IngestionService>();
        services.AddSingleton<Retriever>();
        services.AddSingleton(ProjectCatalog.Load(Path.Combine(paths.DataFolder, "projects.json")));
        services.AddSingleton(OnboardingService.Load(Path.Combine(paths.DataFolder, "onboarding.json")));
        services.AddSingleton<OnboardingService>();
        services.AddSingleton<ChatService>();
        services.AddSingleton(new JsonlStore<FeedbackRecord>(Path.Combine(paths.StateFolder, "feedback.jsonl")));
        services.AddSingleton(new JsonlStore<KnowledgeGap>(Path.Combine(paths.StateFolder, "gaps.jsonl")));
        return services;
    }
}
