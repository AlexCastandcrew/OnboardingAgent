using System.Net;
using Microsoft.AspNetCore.Http.HttpResults;
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

    private static async Task<Results<Ok<ChatResponse>, ValidationProblem, ProblemHttpResult>> Chat(
        ChatRequest request, ProjectCatalog catalog, ChatService chat, ChatOptions options, HttpContext http, ILoggerFactory loggers)
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
            return TypedResults.ValidationProblem(errors);
        }

        // History comes from the client: keep only the last items, only user/assistant roles, and cap each one.
        var history = (request.History ?? [])
            .Where(h => h.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(h.Content))
            .TakeLast(options.MaxHistoryItems)
            .Select(h => new ClaudeMessage(h.Role, h.Content.Length <= options.MaxHistoryItemChars
                ? h.Content.Trim()
                : h.Content[..options.MaxHistoryItemChars].Trim()))
            .ToList();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
        try
        {
            return TypedResults.Ok(await chat.AskAsync(
                new ChatTurn(scope.Project!, role!, scope.Domain, scope.Initiative, message, history, language), timeout.Token));
        }
        catch (LlmException ex)
        {
            return TypedResults.Problem(ex.Message, title: "The model could not answer. Try again.", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested)
        {
            loggers.CreateLogger("Chat").LogWarning(
                "[Chat] Timed out after {Seconds} s for project {ProjectId} role {Role} domain {Domain} initiative {Initiative}",
                options.RequestTimeoutSeconds, scope.Project!.ProjectId, role, scope.Domain, scope.Initiative);
            return TypedResults.Problem($"No answer within {options.RequestTimeoutSeconds} seconds.", title: "The answer took too long. Try again.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
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
