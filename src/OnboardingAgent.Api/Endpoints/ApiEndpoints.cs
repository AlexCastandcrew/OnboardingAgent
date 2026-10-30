using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using OnboardingAgent.Api.RateLimiting;
using OnboardingAgent.Core.Catalog;
using OnboardingAgent.Core.Chat;
using OnboardingAgent.Core.Contracts;
using OnboardingAgent.Core.Ingestion;
using OnboardingAgent.Core.Llm;
using OnboardingAgent.Core.Options;
using OnboardingAgent.Core.Retrieval;
using OnboardingAgent.Core.Sync;

namespace OnboardingAgent.Api.Endpoints;

public static class ApiEndpoints
{
    private const int MaxCommentChars = 500;

    public static void MapApi(this WebApplication app)
    {
        // Every endpoint needs a bearer token except health, which monitoring calls without one.
        var api = app.MapGroup("/api").RequireAuthorization().ProducesProblem(StatusCodes.Status401Unauthorized);

        api.MapGet("/health", Health)
            .AllowAnonymous()
            .WithTags("Health")
            .WithSummary("Service status, index size and configured models. Contains no secrets.");

        api.MapGet("/projects", (ProjectCatalog catalog) => catalog.Projects())
            .WithTags("Catalog")
            .WithSummary("Projects available for onboarding (today only HTG).");

        api.MapGet("/projects/{projectId}/domains", Domains)
            .WithTags("Catalog")
            .WithSummary("Domains (teams) of a project, each with its initiatives (Jira epics).")
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/projects/{projectId}/knowledge", Knowledge)
            .WithTags("Catalog")
            .WithSummary("What the index holds for a project: documents, passages, source types, repositories, teams and initiatives.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/onboarding/{projectId}/{role}", Onboarding)
            .WithTags("Onboarding")
            .WithSummary("The four-step onboarding route for a role, optionally narrowed to a domain and initiative.")
            .ProducesValidationProblem();

        api.MapPost("/chat", Chat)
            .RequireRateLimiting(ChatRateLimit.Policy)
            .WithTags("Chat")
            .WithSummary("Answers a question with the project documents, citing the passages it used.")
            .WithDescription("evidenceStatus is supported, partial or insufficient. A model or timeout failure is a 502/504 error, never an insufficient answer.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        api.MapPost("/chat/stream", ChatStream)
            .RequireRateLimiting(ChatRateLimit.Policy)
            .WithTags("Chat")
            .WithSummary("Same as /api/chat, streamed as Server-Sent Events with the real steps of the pipeline.")
            .WithDescription("Events: activity (a step started, finished or failed; same id, new status), then response (the full chat answer) or error. Validation, 401 and 429 are normal HTTP responses before the stream.")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapGet("/documents/{documentId}", Document)
            .WithTags("Evidence")
            .WithSummary("A corpus document with its metadata and sections, for the local evidence view.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/feedback", Feedback)
            .WithTags("Feedback")
            .WithSummary("Records whether an answer was useful.")
            .ProducesValidationProblem();

        api.MapGet("/gaps", async (JsonlStore<KnowledgeGap> gaps, CancellationToken ct) => await gaps.ReadAllAsync(ct))
            .WithTags("Feedback")
            .WithSummary("Questions answered with insufficient evidence (knowledge gaps).");

        api.MapPost("/admin/reindex", Reindex)
            .WithTags("Admin")
            .WithSummary("Re-runs the ingestion of the corpus folder. Only from the local machine.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapPost("/admin/sync-confluence", SyncConfluence)
            .WithTags("Admin")
            .WithSummary("Copies the listed Confluence pages into the corpus through MCP, then ingests. Only from the local machine.")
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<Results<Ok<ConfluenceSyncReport>, ProblemHttpResult>> SyncConfluence(HttpContext http, ConfluenceSyncRunner runner, CancellationToken ct)
    {
        if (http.Connection.RemoteIpAddress is { } ip && !IPAddress.IsLoopback(ip))
        {
            return TypedResults.Problem("The Confluence sync is only allowed from the local machine.", statusCode: StatusCodes.Status403Forbidden);
        }

        return TypedResults.Ok(await runner.RunAsync(ct));
    }

    private static HealthResponse Health(KnowledgeIndex index, IClaudeClient claude)
    {
        var (documents, chunks, last) = index.Stats();
        var models = claude.Models;
        return new HealthResponse(
            documents > 0 && models.Configured ? "ok" : "degraded",
            new IndexHealth(documents, chunks, last),
            new ModelHealth(models.Region, models.AnswerModelId, models.AuxModelId, models.Auth, models.Configured));
    }

    private static Results<Ok<IReadOnlyList<DomainSummary>>, ProblemHttpResult> Domains(string projectId, string? language, ProjectCatalog catalog)
    {
        var project = catalog.Find(projectId);
        return project is null
            ? TypedResults.Problem($"Unknown project '{projectId}'.", statusCode: StatusCodes.Status404NotFound)
            : TypedResults.Ok(catalog.Domains(project, Languages.Normalize(language)));
    }

    private static Results<Ok<KnowledgeSummary>, ProblemHttpResult> Knowledge(string projectId, ProjectCatalog catalog, KnowledgeIndex index)
    {
        var project = catalog.Find(projectId);
        return project is null
            ? TypedResults.Problem($"Unknown project '{projectId}'.", statusCode: StatusCodes.Status404NotFound)
            : TypedResults.Ok(KnowledgeSummaryBuilder.Build(project, index.GetDocuments(project.ProjectId), index.GetChunkCounts(project.ProjectId)));
    }

    private static Results<Ok<OnboardingRoute>, ValidationProblem> Onboarding(
        string projectId, string role, string? domain, string? initiative, string? language, ProjectCatalog catalog, OnboardingService onboarding)
    {
        var scope = catalog.Validate(projectId, role.ToLowerInvariant(), domain, initiative);
        var errors = new Dictionary<string, string[]>(scope.Errors);
        var lang = Languages.Normalize(language);
        if (lang is not null && !Languages.All.Contains(lang))
        {
            errors["language"] = [LanguageError];
        }

        return errors.Count == 0
            ? TypedResults.Ok(onboarding.Build(scope.Project!.ProjectId, role.ToLowerInvariant(), scope.Domain, scope.Initiative, lang))
            : TypedResults.ValidationProblem(errors);
    }

    private static readonly string LanguageError = $"Language must be one of: {string.Join(", ", Languages.All)}.";

    /// <summary>Validates a chat request (shared by the JSON and the streaming endpoints) and builds the turn, or returns the field errors.</summary>
    private static (ChatTurn? Turn, IDictionary<string, string[]> Errors) PrepareTurn(ChatRequest request, ProjectCatalog catalog, ChatOptions options)
    {
        var role = request.Role?.ToLowerInvariant();
        var scope = catalog.Validate(request.ProjectId, role, request.Domain, request.Initiative);
        var errors = new Dictionary<string, string[]>(scope.Errors);

        var language = Languages.Normalize(request.Language);
        if (language is not null && !Languages.All.Contains(language))
        {
            errors["language"] = [LanguageError];
        }

        var message = request.Message?.Trim() ?? "";
        if (message.Length == 0)
        {
            errors["message"] = ["Message is required."];
        }
        else if (message.Length > options.MaxMessageChars)
        {
            errors["message"] = [$"Message must be at most {options.MaxMessageChars} characters."];
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        // History comes from the client: keep only the last items, only user/assistant roles, and cap each one.
        var history = (request.History ?? [])
            .Where(h => h.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(h.Content))
            .TakeLast(options.MaxHistoryItems)
            .Select(h => new ClaudeMessage(h.Role, h.Content.Length <= options.MaxHistoryItemChars
                ? h.Content.Trim()
                : h.Content[..options.MaxHistoryItemChars].Trim()))
            .ToList();

        return (new ChatTurn(scope.Project!, role!, scope.Domain, scope.Initiative, message, history, language), errors);
    }

    private static async Task<Results<Ok<ChatResponse>, ValidationProblem, ProblemHttpResult>> Chat(
        ChatRequest request, ProjectCatalog catalog, ChatService chat, ChatOptions options, HttpContext http, ILoggerFactory loggers)
    {
        var (turn, errors) = PrepareTurn(request, catalog, options);
        if (turn is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
        try
        {
            return TypedResults.Ok(await chat.AskAsync(turn, timeout.Token));
        }
        catch (LlmException ex)
        {
            return TypedResults.Problem(ex.Message, title: "The model could not answer. Try again.", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested)
        {
            LogTimeout(loggers, options, turn);
            return TypedResults.Problem($"No answer within {options.RequestTimeoutSeconds} seconds.", title: "The answer took too long. Try again.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }

    /// <summary>
    /// Same question and same pipeline as <see cref="Chat"/>, answered as Server-Sent Events: one <c>activity</c> event each
    /// time a real step starts, finishes or fails, then one <c>response</c> event with the full answer (or one <c>error</c>
    /// event). Validation, authentication and rate-limit failures happen before the stream starts and keep their HTTP status.
    /// </summary>
    private static async Task<IResult> ChatStream(
        ChatRequest request, ProjectCatalog catalog, ChatService chat, ChatOptions options, HttpContext http, ILoggerFactory loggers,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions)
    {
        var (turn, errors) = PrepareTurn(request, catalog, options);
        if (turn is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        var json = jsonOptions.Value.SerializerOptions;

        var activities = Channel.CreateUnbounded<ChatActivity>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
        var work = Task.Run(async () =>
        {
            try
            {
                return await chat.AskAsync(turn, timeout.Token, new ChannelProgress(activities.Writer));
            }
            finally
            {
                activities.Writer.TryComplete();
            }
        });

        try
        {
            await foreach (var activity in activities.Reader.ReadAllAsync(http.RequestAborted))
            {
                await WriteEventAsync(http, "activity", activity, json);
            }

            await WriteEventAsync(http, "response", await work, json);
        }
        catch (LlmException ex)
        {
            await WriteEventAsync(http, "error", new ChatStreamError(StatusCodes.Status502BadGateway, "The model could not answer. Try again.", ex.Message), json);
        }
        catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested)
        {
            LogTimeout(loggers, options, turn);
            await WriteEventAsync(http, "error",
                new ChatStreamError(StatusCodes.Status504GatewayTimeout, "The answer took too long. Try again.", $"No answer within {options.RequestTimeoutSeconds} seconds."), json);
        }
        catch (OperationCanceledException)
        {
            // The client left (new conversation, another selection or a closed tab): the linked token already stopped the work.
            await work.ContinueWith(static _ => { }, TaskScheduler.Default);
        }

        return TypedResults.Empty;
    }

    private static void LogTimeout(ILoggerFactory loggers, ChatOptions options, ChatTurn turn) =>
        loggers.CreateLogger("Chat").LogWarning(
            "[Chat] Timed out after {Seconds} s for project {ProjectId} role {Role} domain {Domain} initiative {Initiative}",
            options.RequestTimeoutSeconds, turn.Project.ProjectId, turn.Role, turn.Domain, turn.Initiative);

    private static async Task WriteEventAsync<T>(HttpContext http, string name, T data, JsonSerializerOptions json)
    {
        await http.Response.WriteAsync($"event: {name}\ndata: {JsonSerializer.Serialize(data, json)}\n\n", http.RequestAborted);
        await http.Response.Body.FlushAsync(http.RequestAborted);
    }

    /// <summary>Hands the activities reported by the pipeline to the stream writer without blocking the pipeline.</summary>
    private sealed class ChannelProgress(ChannelWriter<ChatActivity> writer) : IProgress<ChatActivity>
    {
        public void Report(ChatActivity value) => writer.TryWrite(value);
    }

    private static Results<Ok<DocumentView>, ProblemHttpResult> Document(string documentId, KnowledgeIndex index)
    {
        var document = index.GetDocument(documentId);
        if (document is null)
        {
            return TypedResults.Problem($"Unknown document '{documentId}'.", statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(new DocumentView(
            document.DocumentId, document.ProjectId, document.Title, document.SourceType, document.SourceUrl, document.Path,
            document.SourceVersion, document.SourceUpdatedAt, document.IngestedAt, document.Domains, document.Initiatives,
            document.Content,
            index.GetSections(documentId).Select(s => new DocumentSection(s.ChunkId, s.Section)).ToList()));
    }

    private static async Task<Results<NoContent, ValidationProblem>> Feedback(
        FeedbackRequest request, JsonlStore<FeedbackRecord> feedback, TimeProvider clock, CancellationToken ct)
    {
        if (request.Comment is { Length: > MaxCommentChars })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["comment"] = [$"Comment must be at most {MaxCommentChars} characters."]
            });
        }

        await feedback.AppendAsync(new FeedbackRecord(request.AnswerId, request.Helpful, request.Comment?.Trim(), clock.GetUtcNow()), ct);
        return TypedResults.NoContent();
    }

    private static Results<Ok<IngestionResult>, ProblemHttpResult> Reindex(HttpContext http, IngestionService ingestion, CorpusPaths paths)
    {
        if (http.Connection.RemoteIpAddress is { } ip && !IPAddress.IsLoopback(ip))
        {
            return TypedResults.Problem("Reindex is only allowed from the local machine.", statusCode: StatusCodes.Status403Forbidden);
        }

        return TypedResults.Ok(ingestion.Ingest(paths.CorpusFolder, paths.ManifestPath));
    }
}
