using OnboardingAgent.Core.Ingestion;
using OnboardingAgent.Core.Options;
using OnboardingAgent.Core.Retrieval;

namespace OnboardingAgent.Core.UnitTests;

public class IngestionTests
{
    [Fact]
    public void Ingest_SameCorpusTwice_SkipsEverythingAndKeepsChunkIds()
    {
        using var corpus = new TestCorpus()
            .Add("setup", "# Setup\n\n## Requirements\n\nInstall the SDK.\n\n## Run\n\nRun the API.")
            .Add("teams", "# Teams\n\nSilverwing works on Excel Import.");

        var first = corpus.Ingest();
        var sectionsBefore = corpus.Index.GetSections("setup");
        var second = corpus.Ingest();

        Assert.Equal(["setup", "teams"], first.Processed.Order());
        Assert.Empty(second.Processed);
        Assert.Equal(["setup", "teams"], second.Skipped.Order());
        Assert.Equal(sectionsBefore, corpus.Index.GetSections("setup"));
        Assert.Equal(3, corpus.Index.Stats().Chunks);
    }

    [Fact]
    public void Ingest_ChangedDocument_ReplacesItsChunksWithoutDuplicates()
    {
        using var corpus = new TestCorpus().Add("setup", "# Setup\n\nOld text about bananas.");
        corpus.Ingest();

        var result = corpus.Add("setup", "# Setup\n\nNew text about apples.").Ingest();
        var retriever = new Retriever(corpus.Index, new ChatOptions());

        Assert.Equal(["setup"], result.Processed);
        Assert.Equal(1, corpus.Index.Stats().Chunks);
        Assert.Empty(retriever.Search(new RetrievalScope("htg", null, null), ["bananas"]));
        Assert.Single(retriever.Search(new RetrievalScope("htg", null, null), ["apples"]));
    }

    [Fact]
    public void Ingest_DocumentLeftTheManifest_RemovesItButKeepsOneThatFailed()
    {
        using var corpus = new TestCorpus().Add("old", "# Old\n\nText.").Add("broken", "# Broken\n\nText.");
        corpus.Ingest();
        File.Delete(Path.Combine(corpus.Folder, "broken.md"));

        var result = corpus.Drop("old").Ingest();

        Assert.Equal(["old"], result.Removed);
        Assert.Equal("broken", Assert.Single(result.Failed).DocumentId);
        Assert.NotNull(corpus.Index.GetDocument("broken"));
        Assert.Null(corpus.Index.GetDocument("old"));
    }

    [Fact]
    public void Ingest_DocumentNamingItsOwnProject_IsFoundOnlyInThatProject()
    {
        using var corpus = new TestCorpus()
            .Add("teams", "# Teams\n\nSilverwing works on Excel Import.")
            .Add("caps-overview", "# CAPS\n\nCAPS calculates taxes for timecards.", projectId: "caps");

        corpus.Ingest();
        var retriever = new Retriever(corpus.Index, new ChatOptions());

        Assert.Single(retriever.Search(new RetrievalScope("caps", null, null), ["taxes"]));
        Assert.Empty(retriever.Search(new RetrievalScope("htg", null, null), ["taxes"]));
        Assert.Single(retriever.Search(new RetrievalScope("htg", null, null), ["Silverwing"]));
    }

    [Fact]
    public void Ingest_DocumentOfAnotherProjectLeftTheManifest_RemovesItAndKeepsTheRest()
    {
        using var corpus = new TestCorpus()
            .Add("teams", "# Teams\n\nSilverwing works on Excel Import.")
            .Add("caps-overview", "# CAPS\n\nCAPS calculates taxes.", projectId: "caps");
        corpus.Ingest();

        var result = corpus.Drop("caps-overview").Ingest();

        Assert.Equal(["caps-overview"], result.Removed);
        Assert.Null(corpus.Index.GetDocument("caps-overview"));
        Assert.NotNull(corpus.Index.GetDocument("teams"));
    }

    [Fact]
    public void Ingest_InvalidProjectIdOnADocument_FailsWithoutIndexing()
    {
        using var corpus = new TestCorpus().Add("bad", "# Bad\n\nText.", projectId: "Not Valid");

        var result = corpus.Ingest();

        Assert.Empty(result.Processed);
        Assert.Contains(result.Failed, f => f.DocumentId == "bad" && f.Reason.Contains("projectId"));
    }

    [Fact]
    public void Ingest_ExcludedOrEscapingPath_FailsWithoutIndexing()
    {
        using var corpus = new TestCorpus()
            .Add("settings", "# Settings\n\nSecret.", path: "appsettings.md")
            .Add("escape", "# Escape\n\nText.", path: "../outside.md");

        var result = corpus.Ingest();

        Assert.Empty(result.Processed);
        Assert.Contains(result.Failed, f => f.DocumentId == "settings" && f.Reason.Contains("exclusion"));
        Assert.Contains(result.Failed, f => f.DocumentId == "escape" && f.Reason.Contains("outside"));
    }

    [Fact]
    public void Split_LongSection_OverlapsWindowsWithDeterministicIds()
    {
        var paragraphs = string.Join("\n\n", Enumerable.Range(1, 12).Select(i => $"Paragraph {i} " + new string('x', 180)));
        var text = MarkdownChunker.Normalize($"# Doc\n\nIntro.\n\n## Long\n\n{paragraphs}");

        var chunks = MarkdownChunker.Split("doc", "Doc", text, targetChars: 800, overlapChars: 150);
        var longChunks = chunks.Where(c => c.Section == "Doc > Long").ToList();

        Assert.Equal("doc#0", chunks[0].ChunkId);
        Assert.True(longChunks.Count > 1);
        Assert.Equal(longChunks.Select((_, i) => $"doc#1.{i}"), longChunks.Select(c => c.ChunkId));
        // Each window starts with the tail of the previous one.
        Assert.Contains(longChunks[0].Text[^100..], longChunks[1].Text);
    }

    [Fact]
    public void Split_HeadingInsideCodeFence_StaysInTheSection()
    {
        var text = MarkdownChunker.Normalize("# Doc\n\n## Run\n\n```bash\n# not a heading\ndotnet run\n```");

        var chunk = Assert.Single(MarkdownChunker.Split("doc", "Doc", text, 2000, 0));

        Assert.Equal("Doc > Run", chunk.Section);
        Assert.Contains("# not a heading", chunk.Text);
    }
}
