using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace OnboardingAgent.Api.UnitTests;

public class ApiTests
{
    private static readonly object Question = new { projectId = "htg", role = "leader", message = "¿Qué equipo trabaja en el Excel Import?" };

    [Fact]
    public async Task ApiEndpoints_NoToken_EveryEndpointExceptHealthReturns401()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(endpoints);
        foreach (var endpoint in endpoints)
        {
            var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods[0];
            var path = string.Join('/', endpoint.RoutePattern.RawText!.Split('/').Select(s => s.StartsWith('{') ? "x" : s));
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method == "POST")
            {
                request.Content = JsonContent.Create(new { });
            }

            using var response = await client.SendAsync(request);

            var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            Assert.True(anonymous == (path == "/api/health"), $"{method} {path} anonymous={anonymous}");
            Assert.True(anonymous || response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path} returned {(int)response.StatusCode} without a token.");
        }
    }

    [Fact]
    public async Task Chat_TokenForAnotherAudience_Returns401()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token("ana", audience: "some-other-api"));

        using var response = await client.PostAsJsonAsync("/api/chat", Question);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Chat_ValidToken_ReturnsSupportedAnswerWithOnlyTheCitedSource()
    {
        using var factory = new ApiFactory();
        using var client = factory.ClientFor("ana");

        using var response = await client.PostAsJsonAsync("/api/chat", Question);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("supported", root.GetProperty("evidenceStatus").GetString());
        var source = Assert.Single(root.GetProperty("sources").EnumerateArray());
        Assert.Equal("S1", source.GetProperty("id").GetString());
        Assert.Equal("teams", source.GetProperty("documentId").GetString());
        Assert.Equal("HTG teams > Silverwing", source.GetProperty("section").GetString());
    }

    [Fact]
    public async Task Chat_ModelFails_Returns502ProblemInsteadOfInsufficient()
    {
        using var factory = new ApiFactory();
        factory.Claude.FailAnswer = true;
        using var client = factory.ClientFor("ana");

        using var response = await client.PostAsJsonAsync("/api/chat", Question);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(body.RootElement.TryGetProperty("evidenceStatus", out _));
    }

    [Fact]
    public async Task Chat_UnsupportedLanguage_Returns400OnTheLanguageField()
    {
        using var factory = new ApiFactory();
        using var client = factory.ClientFor("ana");

        using var response = await client.PostAsJsonAsync("/api/chat",
            new { projectId = "htg", role = "developer", message = "What is HTG?", language = "fr" });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("language", out _));
    }

    [Fact]
    public async Task Chat_OverTheLimit_Returns429ForThatUserOnly()
    {
        using var factory = new ApiFactory(chatPermitLimit: 2);
        using var ana = factory.ClientFor("ana");
        using var luis = factory.ClientFor("luis");

        var anaStatuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await ana.PostAsJsonAsync("/api/chat", Question);
            anaStatuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Assert.NotNull(response.Headers.RetryAfter);
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            }
        }

        using var luisResponse = await luis.PostAsJsonAsync("/api/chat", Question);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], anaStatuses);
        Assert.Equal(HttpStatusCode.OK, luisResponse.StatusCode);
    }
}
