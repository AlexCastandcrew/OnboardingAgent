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

    private static ChatTurn Turn(string message) => new(Htg, Roles.Developer, null, null, message, []);

    private static string Answer(string text, string status, params string[] sourceIds) =>
        JsonSerializer.Serialize(new
        {
            answer = text,
            evidenceStatus = status,
            claims = sourceIds.Length == 0 ? [] : new[] { new { text, sourceIds } }
        });

    /// <summary>Echoes the question as the rewrite, returns a fixed rerank and the scripted answers in order.</summary>
    private sealed class ScriptedClaude(string[] rerank, params string[] answers) : IClaudeClient
    {
        private int _answer;

        public ModelInfo Models { get; } = new("test", "local", "answer", "aux", "none");

        public bool FailRewrite { get; init; }
        public List<string> AnswerPrompts { get; } = [];

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
            AnswerPrompts.Add(call.Messages[^1].Text);
            return answers[_answer++];
        }
    }
}
