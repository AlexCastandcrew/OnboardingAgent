using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OnboardingAgent.Api.UnitTests;

public class ProgressApiTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static object Progress(string[] done, int total = 4) =>
        new { projectId = "htg", role = "developer", domain = "silverwing", doneSteps = done, totalSteps = total };

    [Fact]
    public async Task Me_NormalToken_IsNotAdmin()
    {
        using var factory = new ApiFactory();
        using var client = factory.ClientFor("ana");

        var me = await client.GetFromJsonAsync<JsonElement>("/api/me", Web);

        Assert.Equal("ana", me.GetProperty("userLabel").GetString());
        Assert.False(me.GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task Me_TokenWithAdminRole_IsAdmin()
    {
        using var factory = new ApiFactory();
        using var client = factory.ClientFor("boss", role: "admin");

        var me = await client.GetFromJsonAsync<JsonElement>("/api/me", Web);

        Assert.True(me.GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task SaveProgress_ThenRead_ReturnsLevelStepsAndNoQuestionText()
    {
        using var factory = new ApiFactory();
        using var client = factory.ClientFor("ana");

        using var saved = await client.PutAsJsonAsync("/api/me/progress", Progress(["overview", "access"]));
        var mine = await client.GetFromJsonAsync<JsonElement>("/api/me/progress", Web);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(2, mine.GetProperty("stepsDone").GetInt32());
        Assert.Equal(4, mine.GetProperty("stepsTotal").GetInt32());
        Assert.Equal(2, mine.GetProperty("level").GetInt32());
        Assert.Equal(1, mine.GetProperty("day").GetInt32());
        Assert.False(mine.TryGetProperty("questions", out _));
    }

    [Fact]
    public async Task SaveProgress_UnknownProject_Returns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.ClientFor("ana");

        using var response = await client.PutAsJsonAsync("/api/me/progress", new { projectId = "nope", role = "developer", doneSteps = new[] { "a" }, totalSteps = 4 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SaveProgress_TooManyStepKeys_Returns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.ClientFor("ana");

        using var response = await client.PutAsJsonAsync("/api/me/progress", Progress(Enumerable.Range(0, 50).Select(i => $"s{i}").ToArray()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminOverview_NormalUser_Returns403()
    {
        using var factory = new ApiFactory();
        using var client = factory.ClientFor("ana");

        using var response = await client.GetAsync("/api/admin/overview");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminOverview_Admin_ListsEveryPersonWithPaceAndWithoutQuestionTexts()
    {
        using var factory = new ApiFactory();
        using var ana = factory.ClientFor("ana");
        using var luis = factory.ClientFor("luis");
        using var boss = factory.ClientFor("boss", role: "admin");
        await ana.PutAsJsonAsync("/api/me/progress", Progress(["overview", "access", "setup", "domain"]));
        await luis.PutAsJsonAsync("/api/me/progress", Progress(["overview"]));

        var overview = await boss.GetFromJsonAsync<JsonElement>("/api/admin/overview", Web);

        var people = overview.GetProperty("people").EnumerateArray().ToList();
        Assert.Equal(["ana", "luis"], people.Select(p => p.GetProperty("person").GetString()!).ToArray());
        Assert.Equal(4, people[0].GetProperty("level").GetInt32());
        Assert.Equal("ahead", people[0].GetProperty("pace").GetString());
        Assert.Equal("silverwing", people[0].GetProperty("team").GetString());
        Assert.Equal(14, overview.GetProperty("targetDays").GetInt32());
        Assert.DoesNotContain("message", overview.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Chat_CountsTheQuestionButNeverStoresIt()
    {
        using var factory = new ApiFactory();
        using var ana = factory.ClientFor("ana");
        using var boss = factory.ClientFor("boss", role: "admin");
        const string secret = "¿Qué equipo trabaja en el Excel Import? (texto privado)";

        using var chat = await ana.PostAsJsonAsync("/api/chat", new { projectId = "htg", role = "leader", message = secret });
        var mine = await ana.GetFromJsonAsync<JsonElement>("/api/me/progress", Web);
        var overview = await boss.GetStringAsync("/api/admin/overview");

        Assert.Equal(HttpStatusCode.OK, chat.StatusCode);
        Assert.Equal(1, mine.GetProperty("questionsAsked").GetInt32());
        Assert.DoesNotContain("texto privado", overview, StringComparison.Ordinal);
    }
}
