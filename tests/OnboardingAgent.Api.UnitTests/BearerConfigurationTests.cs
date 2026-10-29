using Microsoft.Extensions.Configuration;
using OnboardingAgent.Api.Startup;

namespace OnboardingAgent.Api.UnitTests;

public class BearerConfigurationTests
{
    [Fact]
    public void Validate_NoKeysAndNoAudience_ReportsBoth()
    {
        var problems = BearerConfiguration.Validate(Config(new() { ["Authentication:Schemes:Bearer:ValidIssuer"] = "x" })).ToList();

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("SigningKeys"));
        Assert.Contains(problems, p => p.Contains("ValidAudiences"));
    }

    [Fact]
    public void Validate_SigningKeyWithoutValue_StillReportsNoKeys()
    {
        var problems = BearerConfiguration.Validate(Config(new()
        {
            ["Authentication:Schemes:Bearer:SigningKeys:0:Issuer"] = "dotnet-user-jwts",
            ["Authentication:Schemes:Bearer:ValidAudiences:0"] = "http://localhost:5006"
        }));

        Assert.Contains("SigningKeys", Assert.Single(problems));
    }

    [Fact]
    public void Validate_UserJwtsShapeOrAuthorityWithAudience_IsValid()
    {
        var userJwts = Config(new()
        {
            ["Authentication:Schemes:Bearer:SigningKeys:0:Value"] = "a2V5",
            ["Authentication:Schemes:Bearer:ValidAudiences:0"] = "http://localhost:5006"
        });
        var identityProvider = Config(new()
        {
            ["Authentication:Schemes:Bearer:Authority"] = "https://login.example.test/tenant",
            ["Authentication:Schemes:Bearer:Audience"] = "api://onboarding"
        });

        Assert.Empty(BearerConfiguration.Validate(userJwts));
        Assert.Empty(BearerConfiguration.Validate(identityProvider));
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
