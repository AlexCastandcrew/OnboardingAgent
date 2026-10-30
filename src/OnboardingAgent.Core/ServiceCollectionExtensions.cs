using Microsoft.Extensions.DependencyInjection;
using OnboardingAgent.Core.Catalog;
using OnboardingAgent.Core.Chat;
using OnboardingAgent.Core.Contracts;
using OnboardingAgent.Core.Ingestion;
using OnboardingAgent.Core.Options;
using OnboardingAgent.Core.Retrieval;
using OnboardingAgent.Core.Sync;

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
        services.AddSingleton<ConfluenceSyncService>();
        services.AddSingleton<ConfluenceSyncRunner>();
        services.AddSingleton<Retriever>();
        services.AddSingleton(ProjectCatalog.Load(Path.Combine(paths.DataFolder, "projects.json")));
        services.AddSingleton(OnboardingService.Load(Path.Combine(paths.DataFolder, "onboarding.json")));
        services.AddSingleton<OnboardingService>();
        services.AddSingleton<ChatService>();
        services.AddSingleton(new JsonlStore<FeedbackRecord>(Path.Combine(paths.StateFolder, "feedback.jsonl")));
        services.AddSingleton(new JsonlStore<KnowledgeGap>(Path.Combine(paths.StateFolder, "gaps.jsonl")));
        return services;
    }

    /// <summary>Registers per-user onboarding progress (saved in the state folder) for the personal and the admin view.</summary>
    public static IServiceCollection AddProgressTracking(this IServiceCollection services, ProgressOptions progress, CorpusPaths paths)
    {
        services.AddSingleton(progress);
        services.AddSingleton(sp => new Progress.ProgressStore(Path.Combine(paths.StateFolder, "progress.json"), sp.GetRequiredService<TimeProvider>()));
        return services;
    }
}
