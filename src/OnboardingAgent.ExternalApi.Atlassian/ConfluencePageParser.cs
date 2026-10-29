using System.Text.Json;
using OnboardingAgent.Core.Sync;

namespace OnboardingAgent.ExternalApi.Atlassian;

/// <summary>
/// Reads the JSON the Rovo <c>getConfluencePage</c> tool returns (<c>content.nodes[0]</c>). Only title, body, last
/// modified date and space key are taken: the node also carries the author, which must not reach the corpus.
/// </summary>
public static class ConfluencePageParser
{
    public static ConfluencePage Parse(string pageId, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("content", out var content)
                || !content.TryGetProperty("nodes", out var nodes)
                || nodes.ValueKind != JsonValueKind.Array
                || nodes.GetArrayLength() == 0)
            {
                throw new ConfluenceException(ConfluenceFailureKind.Page, "the response has no page (content.nodes is missing or empty); the page may not exist or the account may not see it.");
            }

            var node = nodes[0];
            var title = Text(node, "title");
            var body = Text(node, "body");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
            {
                throw new ConfluenceException(ConfluenceFailureKind.Page, "the response has no title or no body.");
            }

            var space = node.TryGetProperty("space", out var s) && s.ValueKind == JsonValueKind.Object ? Text(s, "key") : null;
            return new ConfluencePage(pageId, title, body, Text(node, "lastModified"), space);
        }
        catch (JsonException)
        {
            throw new ConfluenceException(ConfluenceFailureKind.Page, "the response is not the expected JSON.");
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
