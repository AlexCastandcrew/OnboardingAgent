using Microsoft.Extensions.DependencyInjection;
using OnboardingAgent.Core.Sync;

namespace OnboardingAgent.ExternalApi.Atlassian;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Rovo MCP client as the <see cref="IConfluenceClient"/>. Safe without credentials: the sync then reports why it did not run.</summary>
    public static IServiceCollection AddAtlassianConfluence(this IServiceCollection services, AtlassianOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<IConfluenceClient, RovoMcpConfluenceClient>();
        return services;
    }
}
