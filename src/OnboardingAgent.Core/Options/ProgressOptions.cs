namespace OnboardingAgent.Core.Options;

public sealed class ProgressOptions
{
    public const string Section = "Progress";

    /// <summary>Days a new hire is expected to need to finish the route. The pace compares the progress made with this schedule.</summary>
    public int TargetDays { get; set; } = 14;

    /// <summary>
    /// User names (the token's subject or name) that count as admins in addition to tokens with the role claim "admin".
    /// For local demos only: in a real deployment admins come from the identity provider's role claim.
    /// </summary>
    public List<string> AdminUsers { get; set; } = [];

    /// <summary>
    /// Replaces the sample people (flagged <c>sample</c>) at every start, so the admin view has data to show in a demo.
    /// Never mixed up with real users: each sample row says it is sample data.
    /// </summary>
    public bool SeedSampleData { get; set; }

    public IEnumerable<string> Validate()
    {
        if (TargetDays < 1)
        {
            yield return $"{Section}:TargetDays ({TargetDays}) must be at least 1.";
        }
    }
}
