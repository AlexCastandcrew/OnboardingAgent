using System.Text.Json;
using OnboardingAgent.Core.Contracts;
using OnboardingAgent.Core.Retrieval;

namespace OnboardingAgent.Core.Catalog;

public sealed class OnboardingFile
{
    public List<RouteEntry> Routes { get; set; } = [];
}

/// <summary>
/// A set of steps for a match. Null domain or initiative and role "*" match anything; a route only has
/// to define the steps it changes.
/// </summary>
public sealed class RouteEntry
{
    public string ProjectId { get; set; } = "";
    public string Role { get; set; } = "*";
    public string? Domain { get; set; }
    public string? Initiative { get; set; }
    public List<StepEntry> Steps { get; set; } = [];
}

public sealed class StepEntry
{
    public int Step { get; set; }
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Goal { get; set; } = "";
    public List<string> DocumentIds { get; set; } = [];
    public List<OnboardingLink> Links { get; set; } = [];
    public List<string> SuggestedQuestions { get; set; } = [];
}

/// <summary>
/// Builds the four-step route for project → role → domain → initiative. For each step it takes the most
/// specific route that defines it: initiative over domain over general, then an exact role over "*".
/// A step whose documents are not in the index is returned as "pending", never filled with invented text.
/// </summary>
public sealed class OnboardingService(OnboardingFile file, KnowledgeIndex index)
{
    public const int StepCount = 4;

    public static OnboardingFile Load(string path) =>
        JsonSerializer.Deserialize<OnboardingFile>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException($"'{path}' is empty.");

    public OnboardingRoute Build(string projectId, string role, string? domain, string? initiative)
    {
        var matching = file.Routes
            .Where(r => r.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase)
                        && (r.Role == "*" || r.Role.Equals(role, StringComparison.OrdinalIgnoreCase))
                        && (r.Domain is null || r.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase))
                        && (r.Initiative is null || r.Initiative.Equals(initiative, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(Specificity)
            .ToList();

        var documents = index.GetDocuments(projectId).ToDictionary(d => d.DocumentId, StringComparer.Ordinal);
        var steps = new List<OnboardingStep>();
        for (var number = 1; number <= StepCount; number++)
        {
            var step = matching.SelectMany(r => r.Steps).FirstOrDefault(s => s.Step == number);
            if (step is null)
            {
                steps.Add(new OnboardingStep(number, $"step-{number}", $"Step {number}", "", "pending", [], [], [], []));
                continue;
            }

            var found = step.DocumentIds
                .Where(documents.ContainsKey)
                .Select(id => documents[id])
                .Select(d => new OnboardingDocument(d.DocumentId, d.Title, d.SourceType, d.SourceUrl))
                .ToList();
            steps.Add(new OnboardingStep(
                number,
                step.Key,
                step.Title,
                step.Goal,
                found.Count > 0 ? "ready" : "pending",
                found.Select(d => d.DocumentId).ToList(),
                found,
                step.Links,
                step.SuggestedQuestions));
        }

        return new OnboardingRoute(projectId, role, domain, initiative, steps);
    }

    private static int Specificity(RouteEntry route) =>
        (route.Initiative is null ? 0 : 4) + (route.Domain is null ? 0 : 2) + (route.Role == "*" ? 0 : 1);
}
