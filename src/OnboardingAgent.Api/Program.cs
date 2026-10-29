using System.Text.Json;
using System.Text.Json.Serialization;
using OnboardingAgent.Api.Endpoints;
using OnboardingAgent.Api.OpenApi;
using OnboardingAgent.Api.RateLimiting;
using OnboardingAgent.Api.Startup;
using OnboardingAgent.Core;
using OnboardingAgent.Core.Ingestion;
using OnboardingAgent.Core.Options;
using OnboardingAgent.Core.Sync;
using OnboardingAgent.ExternalApi.Atlassian;
using OnboardingAgent.ExternalApi.Bedrock;

// `dotnet run -- ingest [--corpus <folder>] [--manifest <file>]` runs the ingestion and exits.
// `dotnet run -- sync-confluence` copies the Confluence pages through MCP, then ingests and exits.
var syncOnly = args.Length > 0 && args[0] == "sync-confluence";
var ingestOnly = args.Length > 0 && args[0] == "ingest";
var builder = WebApplication.CreateBuilder(ingestOnly || syncOnly ? args[1..] : args);

var bedrock = builder.Configuration.GetSection(BedrockOptions.Section).Get<BedrockOptions>() ?? new();
var atlassian = builder.Configuration.GetSection(AtlassianOptions.Section).Get<AtlassianOptions>() ?? new();
var corpus = builder.Configuration.GetSection(CorpusOptions.Section).Get<CorpusOptions>() ?? new();
var chat = builder.Configuration.GetSection(ChatOptions.Section).Get<ChatOptions>() ?? new();
var chatRateLimit = builder.Configuration.GetSection(ChatRateLimitOptions.Section).Get<ChatRateLimitOptions>() ?? new();
var paths = CorpusPaths.Resolve(corpus, builder.Environment.ContentRootPath);

// Fail fast with every configuration problem at once, instead of one at a time on the first request.
var problems = corpus.Validate()
    .Concat(paths.Validate())
    .Concat(chat.Validate())
    .Concat(bedrock.Validate())
    .Concat(atlassian.Validate())
    .Concat(chatRateLimit.Validate())
    // The ingest command never serves requests, so it needs no bearer settings.
    .Concat(ingestOnly || syncOnly ? [] : BearerConfiguration.Validate(builder.Configuration))
    .ToList();
if (problems.Count > 0)
{
    Console.Error.WriteLine($"Configuration is invalid ({builder.Environment.EnvironmentName}):");
    foreach (var problem in problems)
    {
        Console.Error.WriteLine($"  - {problem}");
    }

    return 1;
}

builder.Services.AddOnboardingCore(corpus, chat, paths);
builder.Services.AddBedrockClaude(bedrock);
builder.Services.AddAtlassianConfluence(atlassian);

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddProblemDetails();
// Bearer JWT. Issuer, audiences and signing keys come from Authentication:Schemes:Bearer (dotnet user-jwts locally,
// an Authority such as the corporate identity provider elsewhere).
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();
builder.Services.AddChatRateLimit(chatRateLimit);

builder.Services.AddOpenApi(o =>
{
    o.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "AI Team Onboarding Agent API";
        document.Info.Version = "v1";
        document.Info.Description = "Onboarding routes and a RAG chat over the project corpus, answered by Claude on AWS Bedrock.";
        return Task.CompletedTask;
    });
    o.AddDocumentTransformer<BearerSecurityTransformer>();
    o.AddOperationTransformer<BearerSecurityTransformer>();
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

foreach (var warning in bedrock.Warnings())
{
    app.Logger.LogWarning("[Startup] {Warning}", warning);
}

if (syncOnly)
{
    var report = await app.Services.GetRequiredService<ConfluenceSyncRunner>().RunAsync(CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    // The corpus is still served when pages fail, but the exit code says the sync was not clean.
    return report.Sync.Reason is null && report.Sync.Failed.Count == 0 && report.Ingestion?.Failed.Count is null or 0 ? 0 : 1;
}

if (ingestOnly)
{
    var corpusFolder = app.Configuration["corpus"] ?? paths.CorpusFolder;
    var manifest = app.Configuration["manifest"] ?? Path.Combine(corpusFolder, corpus.ManifestFile);
    var result = app.Services.GetRequiredService<IngestionService>().Ingest(corpusFolder, manifest);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    return result.Failed.Count == 0 ? 0 : 1;
}

if (corpus.IngestOnStartup)
{
    var result = app.Services.GetRequiredService<IngestionService>().Ingest(paths.CorpusFolder, paths.ManifestPath);
    app.Logger.LogInformation("[Ingestion] {Processed} processed, {Skipped} skipped, {Removed} removed, {Failed} failed, {Chunks} chunks from {Manifest}",
        result.Processed.Count, result.Skipped.Count, result.Removed.Count, result.Failed.Count, result.ChunkCount, paths.ManifestPath);
    foreach (var failure in result.Failed)
    {
        app.Logger.LogWarning("[Ingestion] Document {DocumentId} in {Manifest} failed: {Reason}", failure.DocumentId, paths.ManifestPath, failure.Reason);
    }
}

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
// After authentication, so the limit is per user.
app.UseRateLimiter();

app.MapOpenApi();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/openapi/v1.json", "Onboarding Agent API v1");
    o.RoutePrefix = "swagger";
    o.EnablePersistAuthorization();
});
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapApi();

app.Run();
return 0;

/// <summary>Entry point, public so the API tests can host it with WebApplicationFactory.</summary>
public partial class Program;
