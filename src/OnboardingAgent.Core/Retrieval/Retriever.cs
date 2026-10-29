using System.Text.RegularExpressions;
using OnboardingAgent.Core.Options;

namespace OnboardingAgent.Core.Retrieval;

public sealed record RetrievalScope(string ProjectId, string? Domain, string? Initiative);

public sealed record Candidate(IndexHit Hit, double Score);

/// <summary>
/// BM25 search over the project's chunks. Documents tagged with the chosen initiative rank first, then the
/// chosen domain, then the rest; nothing is excluded so a cross-cutting question still finds its source.
/// </summary>
public sealed partial class Retriever(KnowledgeIndex index, ChatOptions options)
{
    private const int MaxTerms = 32;

    /// <param name="terms">Keywords and question text, ORed together.</param>
    /// <param name="focus">
    /// The question itself. Identifiers in it (H2G-7165, 7382) get their own search whose hits come first, so a
    /// long document that matches many keywords cannot push the one the question names out of the candidates.
    /// </param>
    public IReadOnlyList<Candidate> Search(RetrievalScope scope, IEnumerable<string> terms, string? focus = null)
    {
        var identifiers = Identifiers(focus ?? "");
        var ranked = Ranked(scope, identifiers).Concat(Ranked(scope, terms));

        // At most MaxChunksPerDocument per document, so one long document cannot fill every slot.
        var perDocument = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<Candidate>();
        foreach (var candidate in ranked)
        {
            var count = perDocument.GetValueOrDefault(candidate.Hit.DocumentId);
            if (count >= options.MaxChunksPerDocument || !seen.Add(candidate.Hit.ChunkId))
            {
                continue;
            }

            perDocument[candidate.Hit.DocumentId] = count + 1;
            candidates.Add(candidate);
            if (candidates.Count == options.CandidateCount)
            {
                break;
            }
        }

        return candidates;
    }

    private IEnumerable<Candidate> Ranked(RetrievalScope scope, IEnumerable<string> terms)
    {
        var match = BuildMatchQuery(terms);
        if (match.Length == 0)
        {
            return [];
        }

        // Over-fetch so the boost and the per-document cap still leave enough candidates.
        return index.Search(scope.ProjectId, match, options.CandidateCount * 3)
            .Select(hit => new Candidate(hit, hit.Bm25Score * Boost(hit, scope)))
            .Where(c => c.Score > options.MinScore)
            .OrderByDescending(c => c.Score);
    }

    /// <summary>Tokens that look like identifiers: letters mixed with digits (H2G-7165), or 3+ digits (7165).</summary>
    internal static IReadOnlyList<string> Identifiers(string text) =>
        Token().Matches(text)
            .Select(m => m.Value)
            .Where(t => t.Any(char.IsDigit) && (t.Any(char.IsLetter) ? t.Length >= 4 : t.Count(char.IsDigit) >= 3))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    private double Boost(IndexHit hit, RetrievalScope scope)
    {
        if (scope.Initiative is not null && hit.Initiatives.Contains(scope.Initiative, StringComparer.OrdinalIgnoreCase))
        {
            return options.InitiativeBoost;
        }

        if (scope.Domain is not null && hit.Domains.Contains(scope.Domain, StringComparer.OrdinalIgnoreCase))
        {
            return options.DomainBoost;
        }

        return 1.0;
    }

    /// <summary>
    /// Turns free text and keywords into an FTS5 OR query. Every term is quoted, so FTS syntax in user input
    /// is inert, and an identifier such as H2G-7165 becomes the phrase "H2G 7165".
    /// </summary>
    public static string BuildMatchQuery(IEnumerable<string> terms)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();
        foreach (var raw in terms)
        {
            foreach (Match token in Token().Matches(raw))
            {
                var term = token.Value.Trim('-', '.', '_', '/');
                if (term.Length < 2 || !seen.Add(term))
                {
                    continue;
                }

                parts.Add('"' + term + '"');
                if (parts.Count == MaxTerms)
                {
                    return string.Join(" OR ", parts);
                }
            }
        }

        return string.Join(" OR ", parts);
    }

    // Words, keeping inner - . _ / so file names, class names and ticket keys stay whole.
    [GeneratedRegex(@"[\p{L}\p{N}]+(?:[-._/][\p{L}\p{N}]+)*")]
    private static partial Regex Token();
}
