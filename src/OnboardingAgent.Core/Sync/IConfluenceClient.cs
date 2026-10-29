namespace OnboardingAgent.Core.Sync;

/// <summary>A Confluence page as the sync needs it. Author and other people fields are deliberately not part of it.</summary>
public sealed record ConfluencePage(string Id, string Title, string Body, string? LastModified, string? SpaceKey);

/// <summary>Why a read failed. <see cref="Auth"/> and <see cref="Connection"/> are not specific to one page, so the sync stops.</summary>
public enum ConfluenceFailureKind
{
    Auth,
    Connection,
    Page
}

public sealed class ConfluenceException(ConfluenceFailureKind kind, string reason, Exception? inner = null) : Exception(reason, inner)
{
    public ConfluenceFailureKind Kind { get; } = kind;
}

/// <summary>Read-only access to Confluence. Implemented over MCP in OnboardingAgent.ExternalApi.Atlassian.</summary>
public interface IConfluenceClient
{
    /// <summary>Null when credentials are present; otherwise why the sync cannot run.</summary>
    string? NotConfiguredReason { get; }

    /// <exception cref="ConfluenceException">Authentication or connection failed.</exception>
    Task<IConfluenceSession> ConnectAsync(CancellationToken cancellationToken);
}

public interface IConfluenceSession : IAsyncDisposable
{
    /// <exception cref="ConfluenceException">The page could not be read.</exception>
    Task<ConfluencePage> ReadPageAsync(string pageId, CancellationToken cancellationToken);
}
