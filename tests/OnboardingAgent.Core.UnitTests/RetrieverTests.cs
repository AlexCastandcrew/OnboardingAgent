using OnboardingAgent.Core.Options;
using OnboardingAgent.Core.Retrieval;

namespace OnboardingAgent.Core.UnitTests;

public class RetrieverTests
{
    [Fact]
    public void Search_ExactTicketKey_MatchesOnlyThatTicket()
    {
        using var corpus = new TestCorpus()
            .Add("jira-h2g-7165", "# H2G-7165\n\nAdds the payroll column mapping.")
            .Add("jira-h2g-7166", "# H2G-7166\n\nAdds the payroll column mapping too.");
        corpus.Ingest();

        var hits = new Retriever(corpus.Index, new ChatOptions()).Search(new RetrievalScope("htg", null, null), ["H2G-7165"]);

        Assert.Equal("jira-h2g-7165", Assert.Single(hits).Hit.DocumentId);
    }

    [Fact]
    public void Search_SameText_RanksInitiativeThenDomainThenGeneral()
    {
        const string text = "# Import\n\nThe Excel import validates each row.";
        using var corpus = new TestCorpus()
            .Add("general", text)
            .Add("domain", text, domains: ["silverwing"])
            .Add("initiative", text, domains: ["silverwing"], initiatives: ["H2G-6893"])
            .Add("other-team", text, domains: ["vhagar"]);
        corpus.Ingest();

        var hits = new Retriever(corpus.Index, new ChatOptions())
            .Search(new RetrievalScope("htg", "silverwing", "H2G-6893"), ["Excel import"]);

        Assert.Equal(["initiative", "domain"], hits.Take(2).Select(h => h.Hit.DocumentId));
        Assert.Equal(4, hits.Count);
    }

    [Fact]
    public void Search_FocusNamesATicket_ItsDocumentEntersAheadOfOneMatchingMoreKeywords()
    {
        // Like the real corpus: one design dense with the generic keywords, and the named design long, with its
        // key once, so on keywords alone BM25 ranks it below every chunk of the other design.
        var sections = string.Join("\n\n", Enumerable.Range(1, 6).Select(i =>
            $"## Part {i}\n\nExcel import mapping columns. The Excel import maps columns; mapping columns drives the import. H2G-7273 part {i}."));
        var filler = string.Join(" ", Enumerable.Repeat("The extractor reads a worksheet log entry from the topic and returns it.", 40));
        using var corpus = new TestCorpus()
            .Add("design-7273", $"# Design\n\n{sections}")
            .Add("design-7165", $"# Design\n\nH2G-7165. {filler}");
        corpus.Ingest();
        var options = new ChatOptions { CandidateCount = 3, ContextChunks = 3, MaxChunksPerDocument = 3 };
        var retriever = new Retriever(corpus.Index, options);
        // Keywords that do not bring the ticket the question names (missing, or drowned out by generic words and
        // other tickets from the history); only the identifier search from the question can find it.
        string[] terms = ["Excel", "import", "mapping", "columns", "H2G-7273"];

        var withoutFocus = retriever.Search(new RetrievalScope("htg", null, null), terms);
        var withFocus = retriever.Search(new RetrievalScope("htg", null, null), terms, focus: "¿Qué cambia el ticket H2G-7165?");

        Assert.DoesNotContain(withoutFocus, c => c.Hit.DocumentId == "design-7165");
        Assert.Equal("design-7165", withFocus[0].Hit.DocumentId);
    }

    [Fact]
    public void Search_LongDocumentMatchingEverywhere_TakesAtMostMaxChunksPerDocument()
    {
        var sections = string.Join("\n\n", Enumerable.Range(1, 8).Select(i => $"## Part {i}\n\nKafka consumer retry topic part {i}."));
        using var corpus = new TestCorpus()
            .Add("long", $"# Long\n\n{sections}")
            .Add("short", "# Short\n\nKafka retry.");
        corpus.Ingest();
        var retriever = new Retriever(corpus.Index, new ChatOptions { CandidateCount = 5, ContextChunks = 5, MaxChunksPerDocument = 2 });

        var hits = retriever.Search(new RetrievalScope("htg", null, null), ["Kafka", "consumer", "retry", "topic"]);

        Assert.Equal(2, hits.Count(h => h.Hit.DocumentId == "long"));
        Assert.Contains(hits, h => h.Hit.DocumentId == "short");
    }

    [Fact]
    public void Identifiers_MixedText_KeepsTicketKeysAndLongNumbersOnly()
    {
        Assert.Equal(["H2G-7165", "7382"], Retriever.Identifiers("¿Qué cambió en H2G-7165 y 7382 en 2 días? v2"));
    }

    [Fact]
    public void BuildMatchQuery_FtsSyntaxInInput_IsQuotedAsPlainTerms()
    {
        var query = Retriever.BuildMatchQuery(["title:secret OR \"x\" NEAR(a b) H2G-7165 *"]);

        Assert.Equal("\"title\" OR \"secret\" OR \"OR\" OR \"NEAR\" OR \"H2G-7165\"", query);
    }
}
