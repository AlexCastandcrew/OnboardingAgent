using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OnboardingAgent.Core.Contracts;
using OnboardingAgent.Core.Options;
using OnboardingAgent.Core.Retrieval;

namespace OnboardingAgent.Core.Ingestion;

/// <summary>
/// Reads a corpus folder and its manifest into the index. Re-running keeps chunk ids, skips documents whose
/// checksum did not change, replaces changed ones and removes documents no longer in the manifest.
/// It only knows the folder and the manifest, not where each file came from.
/// </summary>
public sealed partial class IngestionService(KnowledgeIndex index, CorpusOptions options, TimeProvider clock)
{
    private static readonly JsonSerializerOptions ManifestJson = new() { PropertyNameCaseInsensitive = true };

    public IngestionResult Ingest(string corpusFolder, string manifestPath)
    {
        var processed = new List<string>();
        var skipped = new List<string>();
        var removed = new List<string>();
        var failed = new List<IngestionFailure>();
        var chunkCount = 0;

        CorpusManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CorpusManifest>(File.ReadAllText(manifestPath), ManifestJson);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new IngestionResult([], [], [], [new IngestionFailure(null, $"Cannot read manifest '{manifestPath}': {ex.Message}")], 0);
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.ProjectId))
        {
            return new IngestionResult([], [], [], [new IngestionFailure(null, $"Manifest '{manifestPath}' has no projectId.")], 0);
        }

        var root = Path.GetFullPath(corpusFolder);
        var existing = index.GetChecksums(manifest.ProjectId);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in manifest.Documents)
        {
            var problem = Validate(entry, root, seen);
            if (problem is not null)
            {
                failed.Add(new IngestionFailure(NullIfEmpty(entry.DocumentId), problem));
                continue;
            }

            seen.Add(entry.DocumentId);
            var fullPath = Path.GetFullPath(Path.Combine(root, entry.Path));
            string text;
            try
            {
                text = MarkdownChunker.Normalize(File.ReadAllText(fullPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(new IngestionFailure(entry.DocumentId, $"Cannot read '{entry.Path}': {ex.Message}"));
                continue;
            }

            var checksum = Checksum(manifest.ProjectId, entry, text);
            if (existing.TryGetValue(entry.DocumentId, out var previous) && previous == checksum)
            {
                skipped.Add(entry.DocumentId);
                continue;
            }

            var chunks = MarkdownChunker.Split(entry.DocumentId, entry.Title, text, options.ChunkTargetChars, options.ChunkOverlapChars);
            if (chunks.Count == 0)
            {
                failed.Add(new IngestionFailure(entry.DocumentId, $"'{entry.Path}' has no text to index."));
                continue;
            }

            index.Upsert(
                new IndexedDocument(
                    entry.DocumentId,
                    manifest.ProjectId,
                    entry.Title.Trim(),
                    entry.Path.Replace('\\', '/'),
                    entry.SourceType,
                    NullIfEmpty(entry.SourceUrl),
                    NullIfEmpty(entry.SourceVersion),
                    NullIfEmpty(entry.SourceUpdatedAt),
                    clock.GetUtcNow().ToString("O"),
                    checksum,
                    entry.Domains.Count == 0 ? ["general"] : entry.Domains,
                    entry.Initiatives,
                    text),
                chunks);
            processed.Add(entry.DocumentId);
            chunkCount += chunks.Count;
        }

        // A document that failed validation keeps its previous version; only documents gone from the manifest are removed.
        var failedIds = failed.Select(f => f.DocumentId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var documentId in existing.Keys.Where(id => !seen.Contains(id) && !failedIds.Contains(id)))
        {
            index.Remove(documentId);
            removed.Add(documentId);
        }

        return new IngestionResult(processed, skipped, removed, failed, chunkCount);
    }

    private string? Validate(ManifestDocument entry, string root, HashSet<string> seen)
    {
        if (!DocumentIdFormat().IsMatch(entry.DocumentId))
        {
            return $"documentId '{entry.DocumentId}' must be lowercase letters, digits and dashes.";
        }

        if (seen.Contains(entry.DocumentId))
        {
            return $"documentId '{entry.DocumentId}' appears more than once in the manifest.";
        }

        if (string.IsNullOrWhiteSpace(entry.Title))
        {
            return "title is required.";
        }

        if (!SourceTypes.All.Contains(entry.SourceType))
        {
            return $"sourceType '{entry.SourceType}' must be one of: {string.Join(", ", SourceTypes.All)}.";
        }

        if (string.IsNullOrWhiteSpace(entry.Path) || !entry.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return "path must point to a .md file.";
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, entry.Path));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return $"path '{entry.Path}' is outside the corpus folder.";
        }

        var fileName = Path.GetFileName(fullPath);
        var excluded = options.ExcludedPatterns.FirstOrDefault(p => FileSystemName.MatchesSimpleExpression(p, fileName, ignoreCase: true));
        if (excluded is not null)
        {
            return $"'{entry.Path}' matches the exclusion pattern '{excluded}'.";
        }

        return File.Exists(fullPath) ? null : $"file '{entry.Path}' does not exist.";
    }

    /// <summary>Covers the text and the metadata, so a changed title or domain list is re-indexed too.</summary>
    private static string Checksum(string projectId, ManifestDocument entry, string text)
    {
        var metadata = JsonSerializer.Serialize(new
        {
            projectId,
            entry.Title,
            entry.Path,
            entry.SourceType,
            entry.SourceUrl,
            entry.SourceVersion,
            entry.SourceUpdatedAt,
            entry.Domains,
            entry.Initiatives
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(metadata + "\n" + text)));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,79}$")]
    private static partial Regex DocumentIdFormat();
}
