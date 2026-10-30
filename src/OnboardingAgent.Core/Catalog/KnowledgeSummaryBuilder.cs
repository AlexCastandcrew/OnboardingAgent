using System.Text.RegularExpressions;
using OnboardingAgent.Core.Contracts;
using OnboardingAgent.Core.Retrieval;

namespace OnboardingAgent.Core.Catalog;

/// <summary>Builds the "what is indexed" summary of a project from its indexed documents. Pure: no I/O.</summary>
public static partial class KnowledgeSummaryBuilder
{
    public static KnowledgeSummary Build(ProjectEntry project, IReadOnlyList<IndexedDocument> documents, IReadOnlyDictionary<string, int> passagesByDocument)
    {
        int PassagesOf(IndexedDocument d) => passagesByDocument.GetValueOrDefault(d.DocumentId);

        var sourceTypes = documents
            .GroupBy(d => d.SourceType, StringComparer.Ordinal)
            .Select(g => new SourceTypeSummary(g.Key, g.Count(), g.Sum(PassagesOf)))
            .OrderByDescending(s => s.Documents)
            .ThenBy(s => s.SourceType, StringComparer.Ordinal)
            .ToList();

        var repositories = documents
            .Where(d => d.SourceType == "repo")
            .Select(d => (Document: d, Repository: GitHubRepository(d.SourceUrl)))
            .Where(x => x.Repository is not null)
            .GroupBy(x => x.Repository!.Value.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new RepositorySummary(
                g.Key,
                g.First().Repository!.Value.Url,
                g.Count(),
                g.Select(x => x.Document.SourceVersion).Where(v => v is not null).GroupBy(v => v).OrderByDescending(v => v.Count()).Select(v => v.Key).FirstOrDefault()))
            .OrderByDescending(r => r.Documents)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new KnowledgeSummary(
            project.ProjectId,
            documents.Count,
            documents.Sum(PassagesOf),
            documents.Select(d => d.IngestedAt).Max(StringComparer.Ordinal),
            sourceTypes,
            repositories,
            project.Domains.Count,
            project.Domains.Sum(d => d.Initiatives.Count));
    }

    /// <summary>"owner/repo" and its URL from a GitHub document URL, or null for any other URL.</summary>
    private static (string Name, string Url)? GitHubRepository(string? url)
    {
        var match = url is null ? null : GitHubUrl().Match(url);
        return match is { Success: true } ? ($"{match.Groups[1].Value}/{match.Groups[2].Value}", $"https://github.com/{match.Groups[1].Value}/{match.Groups[2].Value}") : null;
    }

    [GeneratedRegex(@"^https://github\.com/([^/\s]+)/([^/\s]+)/", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubUrl();
}
