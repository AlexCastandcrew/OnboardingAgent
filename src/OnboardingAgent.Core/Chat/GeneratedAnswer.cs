using System.Text.Json;
using System.Text.RegularExpressions;
using OnboardingAgent.Core.Contracts;

namespace OnboardingAgent.Core.Chat;

public sealed partial record GeneratedAnswer(string Answer, EvidenceStatus EvidenceStatus, IReadOnlyList<ChatClaim> Claims)
{
    /// <summary>Every source id the answer uses, from its [S#] markers and from its claims.</summary>
    public IReadOnlyList<string> CitedIds() =>
        CitationMarker().Matches(Answer).SelectMany(m => SourceId().Matches(m.Value).Select(id => id.Value))
            .Concat(Claims.SelectMany(c => c.SourceIds))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(SourceNumber)
            .ToList();

    /// <summary>
    /// Returns why the answer must be rejected, or null when it is valid. This catches invented source ids;
    /// it cannot tell whether a source really supports a claim (that is the manual evaluation's job).
    /// </summary>
    public string? Validate(IReadOnlySet<string> allowedIds)
    {
        if (string.IsNullOrWhiteSpace(Answer))
        {
            return "The answer is empty.";
        }

        var unknown = CitedIds().Where(id => !allowedIds.Contains(id)).ToList();
        if (unknown.Count > 0)
        {
            return $"It cites ids that are not in <sources>: {string.Join(", ", unknown)}.";
        }

        if (EvidenceStatus != EvidenceStatus.Insufficient && CitedIds().Count == 0)
        {
            return $"evidenceStatus is {EvidenceStatus.ToString().ToLowerInvariant()} but no claim cites a source.";
        }

        return null;
    }

    /// <summary>Reads the tool input. Returns null when its shape is wrong.</summary>
    public static GeneratedAnswer? Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object
            || !input.TryGetProperty("answer", out var answer) || answer.ValueKind != JsonValueKind.String
            || !input.TryGetProperty("evidenceStatus", out var status) || status.ValueKind != JsonValueKind.String
            || !Enum.TryParse<EvidenceStatus>(status.GetString(), ignoreCase: true, out var evidenceStatus))
        {
            return null;
        }

        var claims = new List<ChatClaim>();
        if (input.TryGetProperty("claims", out var claimArray) && claimArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var claim in claimArray.EnumerateArray())
            {
                if (claim.ValueKind != JsonValueKind.Object || !claim.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var ids = claim.TryGetProperty("sourceIds", out var idArray) && idArray.ValueKind == JsonValueKind.Array
                    ? idArray.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!.Trim()).ToList()
                    : [];
                claims.Add(new ChatClaim(text.GetString()!, ids));
            }
        }

        return new GeneratedAnswer(answer.GetString()!.Trim(), evidenceStatus, claims);
    }

    private static int SourceNumber(string id) =>
        id.Length > 1 && int.TryParse(id.AsSpan(1), out var n) ? n : int.MaxValue;

    // [S1] and [S1, S2].
    [GeneratedRegex(@"\[\s*S\d+(?:\s*,\s*S\d+)*\s*\]")]
    private static partial Regex CitationMarker();

    [GeneratedRegex(@"S\d+")]
    private static partial Regex SourceId();
}
