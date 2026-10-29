using System.Text.RegularExpressions;

namespace OnboardingAgent.Core.Sync;

/// <summary>
/// Removes personal data from a Confluence page before it is written to the corpus. Person names inside prose cannot
/// be detected; a human reviews the written pages for those.
/// </summary>
public static partial class ConfluenceSanitizer
{
    public static string Sanitize(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var output = new List<string>(lines.Length);
        var inFence = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                output.Add(Scrub(line, inCode: true));
                continue;
            }

            if (inFence)
            {
                output.Add(Scrub(line, inCode: true));
                continue;
            }

            if (PersonLine().IsMatch(line))
            {
                continue;
            }

            if (line.TrimStart().StartsWith('|'))
            {
                var start = i;
                while (i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith('|'))
                {
                    i++;
                }

                output.AddRange(DropPersonColumns(lines[start..(i + 1)]).Select(row => Scrub(row, inCode: false)));
                continue;
            }

            output.Add(Scrub(line, inCode: false));
        }

        return string.Join('\n', output);
    }

    private static string Scrub(string line, bool inCode)
    {
        line = ImageMarkdown().Replace(line, "[diagram omitted]");
        line = ImageHtml().Replace(line, "[diagram omitted]");
        line = MailtoLink().Replace(line, "[email]");
        line = PersonalLink().Replace(line, "[personal link removed]");
        line = Email().Replace(line, "[email]");
        line = Ssn().Replace(line, "[SSN]");
        line = Ein().Replace(line, "[EIN]");
        // Annotations and decorators in code (@Inject) look like mentions.
        return inCode ? line : Mention().Replace(line, "").TrimEnd();
    }

    /// <summary>Drops every column whose header names a person field, in all rows of one table block.</summary>
    private static IEnumerable<string> DropPersonColumns(string[] table)
    {
        var header = SplitRow(table[0]);
        var drop = header.Select((cell, index) => (cell, index))
            .Where(x => PersonHeader().IsMatch(x.cell.Replace("*", "").Replace("_", "").Trim()))
            .Select(x => x.index)
            .ToHashSet();
        if (drop.Count == 0)
        {
            return table;
        }

        // Every column was a person column: nothing left worth keeping.
        return header.Count == drop.Count
            ? []
            : table.Select(row => "| " + string.Join(" | ", SplitRow(row).Where((_, index) => !drop.Contains(index))) + " |").ToList();
    }

    private static List<string> SplitRow(string row)
    {
        var cells = UnescapedPipe().Split(row.Trim()).Select(c => c.Trim()).ToList();
        if (cells.Count > 0 && cells[0].Length == 0)
        {
            cells.RemoveAt(0);
        }

        if (cells.Count > 0 && cells[^1].Length == 0)
        {
            cells.RemoveAt(cells.Count - 1);
        }

        return cells;
    }

    [GeneratedRegex(@"^\s*[>*_\-\s]*(author|authors|owner|owners|reviewer|reviewers|assignee|assignees|assigned to|approver|approvers|reporter|created by|last updated by|updated by|contributors?|dri|stakeholders?)[*_]*\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex PersonLine();

    [GeneratedRegex(@"^(author|authors|owner|owners|reviewer|reviewers|assignee|assignees|assigned to|approver|approvers|reporter|created by|last updated by|updated by|contributors?|dri|stakeholders?)$", RegexOptions.IgnoreCase)]
    private static partial Regex PersonHeader();

    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex UnescapedPipe();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex ImageMarkdown();

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImageHtml();

    [GeneratedRegex(@"\[[^\]]*\]\(mailto:[^)]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex MailtoLink();

    [GeneratedRegex(@"https?://[\w.-]*(sharepoint\.com/personal|-my\.sharepoint\.com|onedrive\.live\.com|1drv\.ms)[^\s)>\]]*", RegexOptions.IgnoreCase)]
    private static partial Regex PersonalLink();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+")]
    private static partial Regex Email();

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b")]
    private static partial Regex Ssn();

    [GeneratedRegex(@"\b\d{2}-\d{7}\b")]
    private static partial Regex Ein();

    [GeneratedRegex(@"(?<![\w.])@[A-Za-z][\w.-]{2,}")]
    private static partial Regex Mention();
}
