namespace OnboardingAgent.Api.Startup;

/// <summary>
/// Checks that the JWT bearer settings can validate a token at all. Without this, a missing key or audience only
/// shows up as every request getting 401.
/// </summary>
public static class BearerConfiguration
{
    public const string Section = "Authentication:Schemes:Bearer";

    public static IEnumerable<string> Validate(IConfiguration configuration)
    {
        var bearer = configuration.GetSection(Section);

        var hasAuthority = !string.IsNullOrWhiteSpace(bearer["Authority"]);
        var hasKeys = bearer.GetSection("SigningKeys").GetChildren().Any(k => !string.IsNullOrWhiteSpace(k["Value"]));
        if (!hasAuthority && !hasKeys)
        {
            yield return $"{Section} has neither Authority nor SigningKeys, so no token can be validated. "
                         + "Locally run `dotnet user-jwts create --project src/OnboardingAgent.Api`; elsewhere set Authority.";
        }

        var hasAudience = !string.IsNullOrWhiteSpace(bearer["Audience"])
                          || !string.IsNullOrWhiteSpace(bearer["ValidAudience"])
                          || bearer.GetSection("ValidAudiences").GetChildren().Any(a => !string.IsNullOrWhiteSpace(a.Value));
        if (!hasAudience)
        {
            yield return $"{Section} has no ValidAudiences, so every token would be rejected.";
        }
    }
}
