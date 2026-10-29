using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OnboardingAgent.Core.Options;

namespace OnboardingAgent.Core.Sync;

public sealed record ConfluencePageSource(string PageId, string DocumentId, string Path);

public sealed record ConfluenceSyncFailure(string PageId, string Reason);

/// <summary>Outcome of a sync run. <see cref="Reason"/> is set when the run did not start or stopped early.</summary>
public sealed record ConfluenceSyncResult(
    IReadOnlyList<string> Synced,
    IReadOnlyList<string> Unchanged,
    IReadOnlyList<ConfluenceSyncFailure> Failed,
    string? Reason);

/// <summary>
/// Copies the Confluence pages listed in corpus/sources/confluence-pages.json into the corpus as sanitized Markdown.
/// It never deletes or truncates: a page that cannot be read keeps its existing file.
/// </summary>
public sealed class ConfluenceSyncService(IConfluenceClient client, CorpusPaths paths, ILogger<ConfluenceSyncService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string SourcesFile = "sources/confluence-pages.json";

    public async Task<ConfluenceSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        if (client.NotConfiguredReason is { } notConfigured)
        {
            logger.LogWarning("[Sync] Confluence sync skipped, the markdown corpus is used as it is: {Reason}", notConfigured);
            return new ConfluenceSyncResult([], [], [], notConfigured);
        }

        var sourcesPath = Path.Combine(paths.CorpusFolder, SourcesFile);
        List<ConfluencePageSource> pages;
        try
        {
            pages = JsonSerializer.Deserialize<SourceList>(File.ReadAllText(sourcesPath), Json)?.Pages ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            var reason = $"Cannot read '{sourcesPath}': {ex.Message}";
            logger.LogWarning("[Sync] {Reason}", reason);
            return new ConfluenceSyncResult([], [], [], reason);
        }

        var synced = new List<string>();
        var unchanged = new List<string>();
        var failed = new List<ConfluenceSyncFailure>();
        IConfluenceSession session;
        try
        {
            session = await client.ConnectAsync(cancellationToken);
        }
        catch (ConfluenceException ex)
        {
            logger.LogWarning("[Sync] Cannot connect to Confluence ({Kind}), no page was synced, existing files kept: {Reason}", ex.Kind, ex.Message);
            return new ConfluenceSyncResult([], [], pages.Select(p => new ConfluenceSyncFailure(p.PageId, $"not attempted: {ex.Message}")).ToList(), ex.Message);
        }

        await using (session)
        {
            for (var i = 0; i < pages.Count; i++)
            {
                var source = pages[i];
                try
                {
                    var page = await session.ReadPageAsync(source.PageId, cancellationToken);
                    (Write(source, page) ? synced : unchanged).Add(source.PageId);
                }
                catch (ConfluenceException ex)
                {
                    logger.LogWarning("[Sync] Page {PageId} ({DocumentId}, {Path}) not synced, existing file kept: {Reason}", source.PageId, source.DocumentId, source.Path, ex.Message);
                    failed.Add(new ConfluenceSyncFailure(source.PageId, ex.Message));
                    if (ex.Kind != ConfluenceFailureKind.Page)
                    {
                        failed.AddRange(pages.Skip(i + 1).Select(p => new ConfluenceSyncFailure(p.PageId, $"not attempted: {ex.Message}")));
                        logger.LogWarning("[Sync] Stopped after page {PageId} ({Kind}); {Remaining} pages not attempted", source.PageId, ex.Kind, pages.Count - i - 1);
                        return new ConfluenceSyncResult(synced, unchanged, failed, ex.Message);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    logger.LogWarning("[Sync] Page {PageId} ({DocumentId}, {Path}) not written, existing file kept: {Reason}", source.PageId, source.DocumentId, source.Path, ex.Message);
                    failed.Add(new ConfluenceSyncFailure(source.PageId, ex.Message));
                }
            }
        }

        logger.LogInformation("[Sync] Confluence: {Synced} synced, {Unchanged} unchanged, {Failed} failed. Review the synced pages for person names before sharing the corpus.",
            synced.Count, unchanged.Count, failed.Count);
        return new ConfluenceSyncResult(synced, unchanged, failed, null);
    }

    /// <summary>Writes the page when its sanitized text differs from the file. Returns false when unchanged.</summary>
    private bool Write(ConfluencePageSource source, ConfluencePage page)
    {
        var root = Path.GetFullPath(paths.CorpusFolder);
        var target = Path.GetFullPath(Path.Combine(root, source.Path));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"path '{source.Path}' must be a .md file inside the corpus folder.");
        }

        var body = ConfluenceSanitizer.Sanitize(page.Body).Trim();
        if (body.Length == 0)
        {
            throw new InvalidOperationException("the page body is empty after sanitizing.");
        }

        var header = new StringBuilder($"# {page.Title.Trim()}\n\nSource: Confluence page {page.Id}");
        if (!string.IsNullOrWhiteSpace(page.SpaceKey))
        {
            header.Append($", space {page.SpaceKey}");
        }

        if (!string.IsNullOrWhiteSpace(page.LastModified))
        {
            header.Append($", last modified {page.LastModified}");
        }

        var text = $"{header}.\n\n{body}\n";
        if (File.Exists(target) && File.ReadAllText(target).Replace("\r\n", "\n") == text)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // Write beside the target and move over it, so a failed write never leaves a truncated page.
        var temp = target + ".sync-tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        File.Move(temp, target, overwrite: true);
        return true;
    }

    private sealed class SourceList
    {
        public List<ConfluencePageSource> Pages { get; set; } = [];
    }
}
