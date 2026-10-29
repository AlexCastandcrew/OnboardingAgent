namespace OnboardingAgent.ExternalApi.Atlassian;

public sealed class AtlassianOptions
{
    public const string Section = "Atlassian";

    /// <summary>Atlassian Rovo MCP server. Per Atlassian's docs (Configuring authentication via API token).</summary>
    public string Endpoint { get; set; } = "https://mcp.atlassian.com/v2/mcp";

    /// <summary>Cloud site passed to the Confluence tool.</summary>
    public string CloudId { get; set; } = "castandcrew.atlassian.net";

    /// <summary>Name of the MCP tool that reads one page.</summary>
    public string PageToolName { get; set; } = "getConfluencePage";

    /// <summary>Atlassian account email of the token owner. Not a secret, but keep it with the token (user secrets or environment).</summary>
    public string? Email { get; set; }

    /// <summary>
    /// Atlassian API token. A secret: keep it in user secrets or the Atlassian__ApiToken environment variable,
    /// never in appsettings.json. Without it the sync does not run and the API serves the markdown corpus as it is.
    /// </summary>
    public string? ApiToken { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    public bool HasCredentials => !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(ApiToken);

    public IEnumerable<string> Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            yield return $"{Section}:Endpoint must be an absolute https URL.";
        }

        foreach (var (name, value) in new[] { (nameof(CloudId), CloudId), (nameof(PageToolName), PageToolName) })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield return $"{Section}:{name} is required.";
            }
        }

        if (TimeoutSeconds < 1)
        {
            yield return $"{Section}:TimeoutSeconds ({TimeoutSeconds}) must be at least 1.";
        }
    }
}
