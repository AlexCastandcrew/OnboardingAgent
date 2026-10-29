using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OnboardingAgent.Core.Sync;

namespace OnboardingAgent.ExternalApi.Atlassian;

/// <summary>
/// Reads Confluence pages through the Atlassian Rovo MCP server with an API token (HTTP Basic, email:token).
/// Read-only: it only calls the page-read tool. The token is never logged.
/// </summary>
public sealed class RovoMcpConfluenceClient(AtlassianOptions options, ILoggerFactory loggerFactory) : IConfluenceClient
{
    public string? NotConfiguredReason => options.HasCredentials
        ? null
        : "Atlassian credentials are not configured (Atlassian:Email and Atlassian:ApiToken).";

    public async Task<IConfluenceSession> ConnectAsync(CancellationToken cancellationToken)
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Email!.Trim()}:{options.ApiToken!.Trim()}"));
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(options.Endpoint),
            Name = "atlassian-rovo",
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Basic {basic}" }
        }, loggerFactory);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            var client = await McpClient.CreateAsync(transport, loggerFactory: loggerFactory, cancellationToken: timeout.Token);
            return new Session(client, options);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw Translate(ex, "connect");
        }
    }

    internal static ConfluenceException Translate(Exception ex, string action)
    {
        var status = ex is HttpRequestException { StatusCode: { } code } ? code : (HttpStatusCode?)null;
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new ConfluenceException(ConfluenceFailureKind.Auth, $"the Atlassian server answered {(int)status} to {action}: the token is invalid or expired, or API token auth is not enabled for the Rovo MCP server.", ex),
            { } other => new ConfluenceException(ConfluenceFailureKind.Connection, $"the Atlassian server answered {(int)other} to {action}.", ex),
            _ when ex is OperationCanceledException => new ConfluenceException(ConfluenceFailureKind.Connection, $"timed out trying to {action}.", ex),
            _ => new ConfluenceException(ConfluenceFailureKind.Connection, $"could not {action} ({ex.GetType().Name}: {ex.Message})", ex)
        };
    }

    private sealed class Session(McpClient client, AtlassianOptions options) : IConfluenceSession
    {
        public async Task<ConfluencePage> ReadPageAsync(string pageId, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            CallToolResult result;
            try
            {
                result = await client.CallToolAsync(options.PageToolName, new Dictionary<string, object?>
                {
                    ["cloudId"] = options.CloudId,
                    ["pageId"] = pageId,
                    ["contentFormat"] = "markdown"
                }, cancellationToken: timeout.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                throw Translate(ex, $"read page {pageId}");
            }

            var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
            if (result.IsError == true)
            {
                var detail = text.Length > 200 ? text[..200] : text;
                throw new ConfluenceException(ConfluenceFailureKind.Page, $"the server reported an error for the page: {detail}");
            }

            return ConfluencePageParser.Parse(pageId, text);
        }

        public ValueTask DisposeAsync() => client.DisposeAsync();
    }
}
