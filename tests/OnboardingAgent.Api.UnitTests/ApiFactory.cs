using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OnboardingAgent.Core.Llm;

namespace OnboardingAgent.Api.UnitTests;

/// <summary>
/// Hosts the real API with a temporary corpus and state folder, a scripted Claude client, and the real JWT
/// bearer handler configured with a test signing key.
/// </summary>
internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    private const string Issuer = "onboarding-tests";
    private const string Audience = "onboarding-api";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "onboarding-api-tests", Guid.NewGuid().ToString("N"));
    private readonly byte[] _signingKey = RandomNumberGenerator.GetBytes(32);
    private readonly int _chatPermitLimit;

    public ApiFactory(int chatPermitLimit = 100)
    {
        _chatPermitLimit = chatPermitLimit;
        var corpus = Path.Combine(_root, "corpus");
        Directory.CreateDirectory(corpus);
        File.WriteAllText(Path.Combine(corpus, "teams.md"),
            "# HTG teams\n\n## Silverwing\n\nSilverwing works on the Excel Import (H2G-6893).");
        File.WriteAllText(Path.Combine(corpus, "manifest.json"), JsonSerializer.Serialize(new
        {
            projectId = "htg",
            documents = new[]
            {
                new { documentId = "teams", path = "teams.md", title = "HTG teams", sourceType = "manual", domains = new[] { "general" } }
            }
        }));
    }

    public ScriptedClaude Claude { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" skips appsettings.Development.json and user secrets, so only the values below apply.
        builder.UseEnvironment("Testing");
        builder.UseSetting("Corpus:Folder", Path.Combine(_root, "corpus"));
        builder.UseSetting("Corpus:StateFolder", Path.Combine(_root, "state"));
        builder.UseSetting("RateLimiting:Chat:PermitLimit", _chatPermitLimit.ToString());
        builder.UseSetting("RateLimiting:Chat:WindowSeconds", "60");
        builder.UseSetting("Authentication:Schemes:Bearer:ValidIssuer", Issuer);
        builder.UseSetting("Authentication:Schemes:Bearer:ValidAudiences:0", Audience);
        builder.UseSetting("Authentication:Schemes:Bearer:SigningKeys:0:Issuer", Issuer);
        builder.UseSetting("Authentication:Schemes:Bearer:SigningKeys:0:Value", Convert.ToBase64String(_signingKey));
        builder.ConfigureTestServices(services => services.AddSingleton<IClaudeClient>(Claude));
    }

    public HttpClient ClientFor(string user)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(user));
        return client;
    }

    public string Token(string user, string audience = Audience) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = audience,
        Subject = new ClaimsIdentity([new Claim("sub", user), new Claim("unique_name", user)]),
        Expires = DateTime.UtcNow.AddMinutes(10),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_signingKey), SecurityAlgorithms.HmacSha256)
    });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // The temp folder is cleaned by the OS eventually.
        }
    }
}

/// <summary>Returns a fixed rewrite, keeps every rerank candidate and answers citing S1, or throws when told to.</summary>
internal sealed class ScriptedClaude : IClaudeClient
{
    public ModelInfo Models { get; } = new("test", "local", "answer", "aux", "none");

    public bool FailAnswer { get; set; }

    public Task<JsonElement> InvokeStructuredAsync(ClaudeStructuredCall call, CancellationToken cancellationToken)
    {
        object result = call.SchemaName switch
        {
            "search_terms" => new { standaloneQuestion = "Which team works on the Excel Import?", keywords = new[] { "Silverwing", "Excel", "Import" } },
            "rank_passages" => new { relevantIds = new[] { "C1" } },
            _ when FailAnswer => throw new LlmException("The model service returned an error (ThrottlingException)."),
            _ => new
            {
                answer = "Silverwing trabaja en el Excel Import [S1].",
                evidenceStatus = "supported",
                claims = new[] { new { text = "Silverwing trabaja en el Excel Import.", sourceIds = new[] { "S1" } } }
            }
        };
        return Task.FromResult(JsonSerializer.SerializeToElement(result));
    }
}
