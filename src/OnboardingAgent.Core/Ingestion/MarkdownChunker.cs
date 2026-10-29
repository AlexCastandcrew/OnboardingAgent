using System.Text;
using System.Text.RegularExpressions;

namespace OnboardingAgent.Core.Ingestion;

public sealed record Chunk(string ChunkId, int Ordinal, string Section, string Text);

/// <summary>
/// Splits normalized Markdown into chunks by heading section. A section longer than the target is split
/// into overlapping windows on paragraph boundaries. Chunk ids are deterministic: documentId#section[.part].
/// </summary>
public static partial class MarkdownChunker
{
    public static string Normalize(string raw)
    {
        var text = raw.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n');
        text = TrailingSpaces().Replace(text, "");
        text = ExtraBlankLines().Replace(text, "\n\n");
        return text.Trim() + "\n";
    }

    public static IReadOnlyList<Chunk> Split(string documentId, string title, string normalized, int targetChars, int overlapChars)
    {
        var chunks = new List<Chunk>();
        var sectionIndex = 0;

        foreach (var (path, body) in Sections(normalized))
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            var section = path.Count == 0 ? title : string.Join(" > ", path);
            var windows = Windows(body.Trim(), targetChars, overlapChars);
            for (var part = 0; part < windows.Count; part++)
            {
                var id = windows.Count == 1 ? $"{documentId}#{sectionIndex}" : $"{documentId}#{sectionIndex}.{part}";
                chunks.Add(new Chunk(id, chunks.Count, section, windows[part]));
            }

            sectionIndex++;
        }

        return chunks;
    }

    private static IEnumerable<(List<string> Path, string Body)> Sections(string text)
    {
        var path = new List<(int Level, string Heading)>();
        var body = new StringBuilder();
        var inFence = false;

        foreach (var line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
            }

            var heading = inFence ? null : HeadingLine().Match(line);
            if (heading is { Success: true })
            {
                yield return (path.Select(p => p.Heading).ToList(), body.ToString());
                body.Clear();

                var level = heading.Groups[1].Value.Length;
                path.RemoveAll(p => p.Level >= level);
                path.Add((level, heading.Groups[2].Value.Trim()));
                continue;
            }

            body.Append(line).Append('\n');
        }

        yield return (path.Select(p => p.Heading).ToList(), body.ToString());
    }

    private static List<string> Windows(string body, int targetChars, int overlapChars)
    {
        if (body.Length <= targetChars)
        {
            return [body];
        }

        var paragraphs = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(p => p.Length <= targetChars ? [p] : HardSplit(p, targetChars))
            .ToList();

        var windows = new List<string>();
        var current = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            if (current.Length > 0 && current.Length + paragraph.Length + 2 > targetChars)
            {
                var done = current.ToString().Trim();
                windows.Add(done);
                current.Clear().Append(Tail(done, overlapChars));
            }

            if (current.Length > 0)
            {
                current.Append("\n\n");
            }

            current.Append(paragraph);
        }

        if (current.Length > 0)
        {
            windows.Add(current.ToString().Trim());
        }

        return windows;
    }

    private static IEnumerable<string> HardSplit(string paragraph, int targetChars)
    {
        var current = new StringBuilder();
        foreach (var word in paragraph.Split(' '))
        {
            if (current.Length > 0 && current.Length + word.Length + 1 > targetChars)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(word);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static string Tail(string text, int overlapChars)
    {
        if (overlapChars <= 0 || text.Length <= overlapChars)
        {
            return overlapChars <= 0 ? "" : text;
        }

        var start = text.Length - overlapChars;
        var space = text.IndexOf(' ', start);
        return space < 0 ? text[start..] : text[(space + 1)..];
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"[ \t]+$", RegexOptions.Multiline)]
    private static partial Regex TrailingSpaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLines();
}
