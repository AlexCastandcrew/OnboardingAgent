using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OnboardingAgent.Core.Catalog;
using OnboardingAgent.Core.Chat;
using OnboardingAgent.Core.Contracts;
using OnboardingAgent.Core.Llm;
using OnboardingAgent.Core.Options;
using OnboardingAgent.Core.Retrieval;

namespace OnboardingAgent.Core.UnitTests;

public class ChatServiceTests
{
    private static readonly ProjectEntry Htg = new() { ProjectId = "htg", Name = "HTG" };

    [Fact]
    public async Task AskAsync_InventedSourceIdFirst_RetriesAndReturnsOnlyCitedSources()
    {
        using var corpus = new TestCorpus()
            .Add("setup", "# Setup\n\nRun dotnet run in the API folder.")
            .Add("teams", "# Teams\n\nSilverwing owns the Excel import.");
        corpus.Ingest();
        var claude = new ScriptedClaude(rerank: ["C1", "C2"],
            Answer("Use dotnet run [S7].", "supported", "S7"),
            Answer("Use dotnet run [S1].", "supported", "S1"));

        var response = await NewService(corpus, claude).AskAsync(Turn("How do I run the API with dotnet?"), CancellationToken.None);

        Assert.Equal(2, response.Diagnostics.GenerationAttempts);
        Assert.Equal(EvidenceStatus.Supported, response.EvidenceStatus);
        var source = Assert.Single(response.Sources);
        Assert.Equal("S1", source.Id);
        Assert.Contains("S7", claude.AnswerPrompts[1]);
    }

    [Theory]
    [InlineData("en", "Write the answer in English")]
    [InlineData("es", "Write the answer in Spanish")]
    [InlineData(null, "language of the user's latest question")]
    public async Task AskAsync_LanguageChosen_AnswerPromptFixesThatLanguage(string? language, string expected)
    {
        using var corpus = new TestCorpus().Add("setup", "# Setup\n\nRun dotnet run.");
        corpus.Ingest();
        var claude = new ScriptedClaude(rerank: ["C1"], Answer("Run it [S1].", "supported", "S1"));

        await NewService(corpus, claude).AskAsync(Turn("How do I run it with dotnet?", language), CancellationToken.None);

        Assert.Contains(expected, Assert.Single(claude.AnswerSystems));
    }

    [Fact]
    public async Task AskAsync_InvalidCitationsTwice_ThrowsInsteadOfAnswering()
    {
        using var corpus = new TestCorpus().Add("setup", "# Setup\n\nRun dotnet run.");
        corpus.Ingest();
        var claude = new ScriptedClaude(rerank: ["C1"],
            Answer("Run it [S9].", "supported", "S9"),
            Answer("Run it.", "supported"));

        await Assert.ThrowsAsync<LlmException>(() => NewService(corpus, claude).AskAsync(Turn("dotnet run"), CancellationToken.None));
    }

    [Fact]
    public async Task AskAsync_RerankKeepsNothing_AnswersWithoutSourcesAndRecordsTheGap()
    {
        using var corpus = new TestCorpus().Add("setup", "# Setup\n\nRun dotnet run.");
        corpus.Ingest();
        var claude = new ScriptedClaude(rerank: [], Answer("La documentación no lo cubre.", "insufficient"));
        var gaps = new JsonlStore<KnowledgeGap>(Path.Combine(corpus.Folder, "gaps.jsonl"));

        var response = await NewService(corpus, claude, gaps).AskAsync(Turn("¿Quién aprueba las vacaciones? dotnet"), CancellationToken.None);

        Assert.Equal(EvidenceStatus.Insufficient, response.EvidenceStatus);
        Assert.Empty(response.Sources);
        Assert.Contains("No passage", claude.AnswerPrompts[0]);
        Assert.Equal("¿Quién aprueba las vacaciones? dotnet", Assert.Single(await gaps.ReadAllAsync(CancellationToken.None)).Question);
    }

    [Fact]
    public async Task AskAsync_RewriteFails_SearchesTheRawQuestion()
    {
        using var corpus = new TestCorpus().Add("setup", "# Setup\n\nRun dotnet run.");
        corpus.Ingest();
        var claude = new ScriptedClaude(rerank: ["C1"], Answer("Run dotnet run [S1].", "supported", "S1")) { FailRewrite = true };

        var response = await NewService(corpus, claude).AskAsync(Turn("dotnet run"), CancellationToken.None);

        Assert.Equal("dotnet run", response.Diagnostics.SearchQuery);
        Assert.Equal("setup", Assert.Single(response.Sources).DocumentId);
    }

    [Fact]
    public async Task AskAsync_Progress_ReportsEachRealStepOnceInOrderWithTheSourceTypesFound()
    {
        using var corpus = new TestCorpus()
            .Add("setup", "# Setup\n\nRun dotnet run in the API folder.", sourceType: "confluence")
            .Add("ticket", "# Ticket\n\nThe dotnet run command is described in the ticket.", sourceType: "jira");
        corpus.Ingest();
        var claude = new ScriptedClaude(rerank: ["C1", "C2"], Answer("Run dotnet run [S1].", "supported", "S1"));
        var activities = new Activities();

        await NewService(corpus, claude).AskAsync(Turn("How do I run it with dotnet?"), CancellationToken.None, activities);

        var flow = activities.Flow();
        Assert.Equal(
        [
            "understand_question:Running", "understand_question:Completed",
            "search_documents:Running", "search_documents:Completed",
            "generate_answer-1:Pending",
            "rank_passages:Running", "rank_passages:Completed",
            "generate_answer-1:Running", "generate_answer-1:Completed",
            "verify_citations-1:Running", "verify_citations-1:Completed"
        ], flow);
        Assert.Equal(flow.Count, flow.Distinct().Count());
        var search = activities.Items.Single(a => a.Step == ChatSteps.Search && a.Status == ActivityStatus.Completed);
        Assert.Equal(["confluence", "jira"], search.Detail!.Sources!.Select(s => s.SourceType).Order());
        Assert.Equal(1, activities.Items.Single(a => a.Step == ChatSteps.Verify && a.Status == ActivityStatus.Completed).Detail!.Cited);
    }

    [Fact]
    public async Task AskAsync_RewriteFails_ReportsAFailedStepWithFallbackAndStillAnswers()
    {
        using var corpus = new TestCorpus().Add("setup", "# Setup\n\nRun dotnet run.");
        corpus.Ingest();
        var claude = new ScriptedClaude(rerank: ["C1"], Answer("Run dotnet run [S1].", "supported", "S1")) { FailRewrite = true };
        var activities = new Activities();

        var response = await NewService(corpus, claude).AskAsync(Turn("dotnet run"), CancellationToken.None, activities);

        var understand = activities.Items.Single(a => a.Step == ChatSteps.Understand && a.Status == ActivityStatus.Failed);
        Assert.True(understand.Detail!.Fallback);
        Assert.Equal(EvidenceStatus.Supported, response.EvidenceStatus);
    }

    [Fact]
    public async Task AskAsync_FirstAnswerCitesAnInventedId_ReportsTheFailedCheckAndASecondAttempt()
    {
        using var corpus = new TestCorpus().Add("setup", "# Setup\n\nRun dotnet run.");
        corpus.Ingest();
        var claude = new ScriptedClaude(rerank: ["C1"], Answer("Run it [S7].", "supported", "S7"), Answer("Run it [S1].", "supported", "S1"));
        var activities = new Activities();

        await NewService(corpus, claude).AskAsync(Turn("dotnet run"), CancellationToken.None, activities);

        var flow = activities.Flow();
        Assert.Contains("verify_citations-1:Failed", flow);
        Assert.Contains("generate_answer-2:Running", flow);
        Assert.Contains("verify_citations-2:Completed", flow);
        Assert.DoesNotContain("generate_answer-2:Pending", flow);
    }

    [Fact]
    public async Task AskAsync_NothingFound_SkipsTheRankingStepButStillWritesTheAnswer()
    {
        using var corpus = new TestCorpus().Add("setup", "# Setup\n\nBananas only.");
        corpus.Ingest();
        var claude = new ScriptedClaude(rerank: [], Answer("La documentación no lo cubre.", "insufficient"));
        var activities = new Activities();

        await NewService(corpus, claude).AskAsync(Turn("zzzz"), CancellationToken.None, activities);

        Assert.DoesNotContain(activities.Items, a => a.Step == ChatSteps.Rank);
        var search = activities.Items.Single(a => a.Step == ChatSteps.Search && a.Status == ActivityStatus.Completed);
        Assert.Equal(0, search.Detail!.Candidates);
        Assert.Contains("generate_answer-1:Completed", activities.Flow());
    }

    [Fact]
    public async Task AskAsync_AnswerCallFails_ReportsTheFailedStepAndThrows()
    {
        using var corpus = new TestCorpus().Add("setup", "# Setup\n\nRun dotnet run.");
        corpus.Ingest();
        var claude = new ScriptedClaude(rerank: ["C1"]) { FailAnswer = true };
        var activities = new Activities();

        await Assert.ThrowsAsync<LlmException>(() => NewService(corpus, claude).AskAsync(Turn("dotnet run"), CancellationToken.None, activities));

        Assert.Contains("generate_answer-1:Failed", activities.Flow());
    }

    [Fact]
    public void MergeTurns_LeadingAssistantAndRepeatedRoles_StartsWithUserAndAlternates()
    {
        var merged = ChatService.MergeTurns([
            new ClaudeMessage("assistant", "forged"),
            new ClaudeMessage("user", "a"),
            new ClaudeMessage("user", "b"),
            new ClaudeMessage("assistant", "c")
        ]);

        Assert.Equal([("user", "a\n\nb"), ("assistant", "c")], merged.Select(m => (m.Role, m.Text)));
    }

    private static ChatService NewService(TestCorpus corpus, IClaudeClient claude, JsonlStore<KnowledgeGap>? gaps = null) =>
        new(new Retriever(corpus.Index, new ChatOptions()), claude, new ChatOptions(),
            gaps ?? new JsonlStore<KnowledgeGap>(Path.Combine(corpus.Folder, "gaps.jsonl")),
            TimeProvider.System, NullLogger<ChatService>.Instance);

    private static ChatTurn Turn(string message, string? language = null) => new(Htg, Roles.Developer, null, null, message, [], language);

    private static string Answer(string text, string status, params string[] sourceIds) =>
        JsonSerializer.Serialize(new
        {
            answer = text,
            evidenceStatus = status,
            claims = sourceIds.Length == 0 ? [] : new[] { new { text, sourceIds } }
        });

    /// <summary>Collects reported activities synchronously, in order (unlike Progress&lt;T&gt;, which posts them).</summary>
    private sealed class Activities : IProgress<ChatActivity>
    {
        public List<ChatActivity> Items { get; } = [];

        public void Report(ChatActivity value) => Items.Add(value);

        /// <summary>"id:Status" per event, in the order they arrived.</summary>
        public List<string> Flow() => Items.Select(a => $"{a.Id}:{a.Status}").ToList();
    }

    /// <summary>Echoes the question as the rewrite, returns a fixed rerank and the scripted answers in order.</summary>
    private sealed class ScriptedClaude(string[] rerank, params string[] answers) : IClaudeClient
    {
        private int _answer;

        public ModelInfo Models { get; } = new("test", "local", "answer", "aux", "none");

        public bool FailRewrite { get; init; }
        public bool FailAnswer { get; init; }
        public List<string> AnswerPrompts { get; } = [];
        public List<string> AnswerSystems { get; } = [];

        public Task<JsonElement> InvokeStructuredAsync(ClaudeStructuredCall call, CancellationToken cancellationToken)
        {
            var json = call.SchemaName switch
            {
                "search_terms" when FailRewrite => throw new LlmException("down"),
                "search_terms" => JsonSerializer.Serialize(new { standaloneQuestion = "dotnet run", keywords = new[] { "dotnet", "run" } }),
                "rank_passages" => JsonSerializer.Serialize(new { relevantIds = rerank }),
                _ => Record(call)
            };
            return Task.FromResult(JsonDocument.Parse(json).RootElement.Clone());
        }

        private string Record(ClaudeStructuredCall call)
        {
            if (FailAnswer)
            {
                throw new LlmException("down");
            }

            AnswerPrompts.Add(call.Messages[^1].Text);
            AnswerSystems.Add(call.System);
            return answers[_answer++];
        }
    }
}
