using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Security;
using Microsoft.Extensions.Logging;
using OnboardingAgent.Core.Catalog;
using OnboardingAgent.Core.Contracts;
using OnboardingAgent.Core.Llm;
using OnboardingAgent.Core.Options;
using OnboardingAgent.Core.Retrieval;

namespace OnboardingAgent.Core.Chat;

/// <summary>A chat request that already passed validation, with domain and initiative in canonical form.</summary>
public sealed record ChatTurn(
    ProjectEntry Project,
    string Role,
    string? Domain,
    string? Initiative,
    string Message,
    IReadOnlyList<ClaudeMessage> History);

/// <summary>
/// Question + short history → query rewrite (Haiku) → BM25 → rerank (Haiku) → 4–6 passages →
/// structured answer (Sonnet) → citation check (one retry) → answer with its sources.
/// </summary>
public sealed class ChatService(
    Retriever retriever,
    IClaudeClient claude,
    ChatOptions options,
    JsonlStore<KnowledgeGap> gaps,
    TimeProvider clock,
    ILogger<ChatService> logger)
{
    private const int MaxGenerationAttempts = 2;
    private const int RerankExcerptChars = 500;
    private const int SourceExcerptChars = 1200;

    public async Task<ChatResponse> AskAsync(ChatTurn turn, CancellationToken cancellationToken)
    {
        var answerId = Guid.NewGuid();
        var started = Stopwatch.GetTimestamp();
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["AnswerId"] = answerId,
            ["ProjectId"] = turn.Project.ProjectId,
            ["Role"] = turn.Role,
            ["Domain"] = turn.Domain,
            ["Initiative"] = turn.Initiative
        });

        var (searchQuery, keywords) = await RewriteAsync(turn, cancellationToken);
        var candidates = retriever.Search(
            new RetrievalScope(turn.Project.ProjectId, turn.Domain, turn.Initiative),
            keywords.Prepend(searchQuery).Append(turn.Message),
            focus: searchQuery + "\n" + turn.Message);

        var (context, reranked) = await RerankAsync(searchQuery, candidates, cancellationToken);
        var sources = context
            .Select((c, i) => (Id: $"S{i + 1}", Candidate: c))
            .ToList();

        var (generated, attempts) = await GenerateAsync(turn, sources, cancellationToken);

        var cited = generated.CitedIds().ToHashSet(StringComparer.Ordinal);
        var response = new ChatResponse(
            answerId,
            generated.Answer,
            generated.EvidenceStatus,
            sources.Where(s => cited.Contains(s.Id)).Select(s => ToSource(s.Id, s.Candidate.Hit)).ToList(),
            generated.Claims,
            new ChatDiagnostics(searchQuery, candidates.Count, sources.Count, reranked, attempts,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds));

        logger.LogInformation(
            "[Chat] Answered with {EvidenceStatus}: {Candidates} candidates, {Context} in context, {Cited} cited, {Attempts} attempts, {ElapsedMs} ms",
            response.EvidenceStatus, candidates.Count, sources.Count, response.Sources.Count, attempts, response.Diagnostics.ElapsedMs);

        if (response.EvidenceStatus == EvidenceStatus.Insufficient)
        {
            // Only the question is kept, not the conversation.
            await gaps.AppendAsync(
                new KnowledgeGap(answerId, turn.Project.ProjectId, turn.Role, turn.Domain, turn.Initiative, turn.Message, clock.GetUtcNow()),
                cancellationToken);
        }

        return response;
    }

    /// <summary>
    /// Resolves references from the history and turns the question into English plus original-language keywords,
    /// since BM25 does not cross languages. If the rewrite fails the raw question is searched instead.
    /// </summary>
    private async Task<(string Query, IReadOnlyList<string> Keywords)> RewriteAsync(ChatTurn turn, CancellationToken cancellationToken)
    {
        var prompt = new StringBuilder();
        if (turn.History.Count > 0)
        {
            prompt.AppendLine("<history>");
            foreach (var item in turn.History)
            {
                prompt.Append(item.Role).Append(": ").AppendLine(item.Text);
            }

            prompt.AppendLine("</history>");
        }

        prompt.Append("<question>").Append(turn.Message).AppendLine("</question>");

        try
        {
            var result = await claude.InvokeStructuredAsync(
                new ClaudeStructuredCall(ModelTier.Aux, Prompts.RewriteSystem, [new ClaudeMessage("user", prompt.ToString())],
                    Prompts.RewriteSchemaName, "Return the standalone question and search keywords.", Prompts.RewriteSchema),
                cancellationToken);

            var query = result.TryGetProperty("standaloneQuestion", out var q) && q.ValueKind == JsonValueKind.String
                ? q.GetString()!.Trim()
                : turn.Message;
            var keywords = result.TryGetProperty("keywords", out var k) && k.ValueKind == JsonValueKind.Array
                ? k.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
                : [];
            return (query.Length == 0 ? turn.Message : query, keywords);
        }
        catch (LlmException ex)
        {
            logger.LogWarning(ex, "[Chat] Query rewrite failed, searching the raw question of {Length} characters with {HistoryItems} history items",
                turn.Message.Length, turn.History.Count);
            return (turn.Message, []);
        }
    }

    /// <summary>
    /// Haiku keeps the passages that help and orders them. An empty selection is a valid result and leads to
    /// "insufficient". If the rerank call fails, the BM25 order is used.
    /// </summary>
    private async Task<(IReadOnlyList<Candidate> Context, bool Reranked)> RerankAsync(
        string question, IReadOnlyList<Candidate> candidates, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return ([], false);
        }

        var labeled = candidates.Select((c, i) => (Id: $"C{i + 1}", Candidate: c)).ToList();
        var prompt = new StringBuilder();
        prompt.Append("<question>").Append(question).AppendLine("</question>").AppendLine("<passages>");
        foreach (var (id, candidate) in labeled)
        {
            prompt.Append($"<passage id=\"{id}\" title=\"{Attr(candidate.Hit.Title)}\" section=\"{Attr(candidate.Hit.Section)}\">")
                .Append(SecurityElement.Escape(Truncate(candidate.Hit.Text, RerankExcerptChars)))
                .AppendLine("</passage>");
        }

        prompt.AppendLine("</passages>");

        try
        {
            var result = await claude.InvokeStructuredAsync(
                new ClaudeStructuredCall(ModelTier.Aux, Prompts.RerankSystem, [new ClaudeMessage("user", prompt.ToString())],
                    Prompts.RerankSchemaName, "Return the ids of the relevant passages, most relevant first.", Prompts.RerankSchema),
                cancellationToken);

            var byId = labeled.ToDictionary(l => l.Id, l => l.Candidate, StringComparer.OrdinalIgnoreCase);
            var ordered = result.TryGetProperty("relevantIds", out var ids) && ids.ValueKind == JsonValueKind.Array
                ? ids.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(byId.ContainsKey)
                    .Select(id => byId[id])
                    .Take(options.ContextChunks)
                    .ToList()
                : [];

            // Ids only, never passage text: enough to see whether retrieval or the rerank lost a document.
            logger.LogDebug("[Chat] Rerank kept [{Kept}] of candidates [{Candidates}]",
                string.Join(", ", ordered.Select(c => c.Hit.ChunkId)),
                string.Join(", ", labeled.Select(l => $"{l.Id}={l.Candidate.Hit.ChunkId}:{l.Candidate.Score:0.0}")));
            return (ordered, true);
        }
        catch (LlmException ex)
        {
            logger.LogWarning(ex, "[Chat] Rerank failed, using BM25 order for {Candidates} candidates, top chunk {TopChunk}",
                candidates.Count, candidates[0].Hit.ChunkId);
            return (candidates.Take(options.ContextChunks).ToList(), false);
        }
    }

    private async Task<(GeneratedAnswer Answer, int Attempts)> GenerateAsync(
        ChatTurn turn, IReadOnlyList<(string Id, Candidate Candidate)> sources, CancellationToken cancellationToken)
    {
        var allowed = sources.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var userTurn = BuildUserTurn(turn, sources);
        string? rejection = null;

        for (var attempt = 1; attempt <= MaxGenerationAttempts; attempt++)
        {
            var text = rejection is null
                ? userTurn
                : userTurn + $"\n\nYour previous answer was rejected: {rejection} Answer again following the rules.";
            var messages = MergeTurns(turn.History.Append(new ClaudeMessage("user", text)));

            var result = await claude.InvokeStructuredAsync(
                new ClaudeStructuredCall(ModelTier.Answer, Prompts.AnswerSystem(turn.Project.Name), messages,
                    Prompts.AnswerSchemaName, "Submit the answer, its evidence status and its claims with source ids.", Prompts.AnswerSchema),
                cancellationToken);

            var generated = GeneratedAnswer.Parse(result);
            rejection = generated is null ? "The tool input did not match the schema." : generated.Validate(allowed);
            if (rejection is null)
            {
                return (generated!, attempt);
            }

            logger.LogWarning("[Chat] Answer attempt {Attempt} rejected: {Reason} Allowed ids: {Allowed}",
                attempt, rejection, string.Join(",", allowed));
        }

        throw new LlmException($"The model returned an answer with invalid citations twice. {rejection}");
    }

    private static string BuildUserTurn(ChatTurn turn, IReadOnlyList<(string Id, Candidate Candidate)> sources)
    {
        var initiative = turn.Initiative is null
            ? null
            : turn.Project.Domains.SelectMany(d => d.Initiatives).FirstOrDefault(i => i.InitiativeId == turn.Initiative);

        var text = new StringBuilder();
        text.Append($"<context project=\"{Attr(turn.Project.Name)}\" role=\"{turn.Role}\" domain=\"{Attr(turn.Domain ?? "general")}\"");
        if (initiative is not null)
        {
            text.Append($" initiative=\"{Attr(initiative.InitiativeId)} {Attr(initiative.Name)}\"");
        }

        text.AppendLine(" />");
        if (sources.Count == 0)
        {
            text.AppendLine("<sources>No passage in the documentation matched this question.</sources>");
        }
        else
        {
            text.AppendLine("<sources>");
            foreach (var (id, candidate) in sources)
            {
                var hit = candidate.Hit;
                text.Append($"<source id=\"{id}\" title=\"{Attr(hit.Title)}\" section=\"{Attr(hit.Section)}\" type=\"{hit.SourceType}\"");
                if (hit.SourceVersion is not null)
                {
                    text.Append($" version=\"{Attr(hit.SourceVersion)}\"");
                }

                text.Append('>').Append(SecurityElement.Escape(hit.Text)).AppendLine("</source>");
            }

            text.AppendLine("</sources>");
        }

        text.Append("<question>").Append(SecurityElement.Escape(turn.Message)).Append("</question>");
        return text.ToString();
    }

    /// <summary>Converse needs alternating turns that start with the user: drop leading assistant turns and merge neighbours.</summary>
    internal static IReadOnlyList<ClaudeMessage> MergeTurns(IEnumerable<ClaudeMessage> turns)
    {
        var merged = new List<ClaudeMessage>();
        foreach (var turn in turns.SkipWhile(t => t.Role != "user"))
        {
            if (merged.Count > 0 && merged[^1].Role == turn.Role)
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + "\n\n" + turn.Text };
            }
            else
            {
                merged.Add(turn);
            }
        }

        return merged;
    }

    private static ChatSource ToSource(string id, IndexHit hit) => new(
        id, hit.DocumentId, hit.ChunkId, hit.Title, hit.SourceUrl, hit.Section, Truncate(hit.Text, SourceExcerptChars),
        hit.SourceType, hit.SourceVersion, hit.SourceUpdatedAt);

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private static string Attr(string value) => SecurityElement.Escape(value) ?? "";
}
