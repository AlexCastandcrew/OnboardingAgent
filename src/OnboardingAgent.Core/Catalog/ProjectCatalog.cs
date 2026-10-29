using System.Text.Json;
using OnboardingAgent.Core.Contracts;

namespace OnboardingAgent.Core.Catalog;

public sealed class CatalogFile
{
    public List<ProjectEntry> Projects { get; set; } = [];
}

public sealed class ProjectEntry
{
    public string ProjectId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? RepoUrl { get; set; }

    /// <summary>Date of the team and initiative snapshot; epics and assignments change.</summary>
    public string SnapshotDate { get; set; } = "";

    public List<DomainEntry> Domains { get; set; } = [];
}

public sealed class DomainEntry
{
    public string DomainId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Depth { get; set; } = "overview";
    public List<InitiativeEntry> Initiatives { get; set; } = [];
}

public sealed class InitiativeEntry
{
    public string InitiativeId { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>Projects, their domains (teams) and initiatives (Jira epics), read once from data/projects.json.</summary>
public sealed class ProjectCatalog
{
    private readonly Dictionary<string, ProjectEntry> _projects;

    public ProjectCatalog(CatalogFile file)
    {
        _projects = file.Projects.ToDictionary(p => p.ProjectId, StringComparer.OrdinalIgnoreCase);
    }

    public static ProjectCatalog Load(string path) =>
        new(JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"'{path}' is empty."));

    public IReadOnlyList<ProjectSummary> Projects() =>
        _projects.Values.Select(p => new ProjectSummary(p.ProjectId, p.Name, p.Description, p.RepoUrl, p.SnapshotDate)).ToList();

    public ProjectEntry? Find(string projectId) => _projects.GetValueOrDefault(projectId);

    public IReadOnlyList<DomainSummary> Domains(ProjectEntry project) =>
        project.Domains
            .Select(d => new DomainSummary(d.DomainId, d.Name, d.Depth,
                d.Initiatives.Select(i => new InitiativeSummary(i.InitiativeId, i.Name)).ToList()))
            .ToList();

    /// <summary>
    /// Checks project, role, domain and initiative together. Returns field → error for a 400 response,
    /// empty when valid. Domain and initiative come back in their canonical casing.
    /// </summary>
    public ScopeValidation Validate(string? projectId, string? role, string? domain, string? initiative)
    {
        var errors = new Dictionary<string, string[]>();
        var project = string.IsNullOrWhiteSpace(projectId) ? null : Find(projectId);
        if (project is null)
        {
            errors["projectId"] = [$"Unknown project '{projectId}'."];
            return new ScopeValidation(errors, null, null, null);
        }

        if (role is null || !Roles.All.Contains(role))
        {
            errors["role"] = [$"Role must be one of: {string.Join(", ", Roles.All)}."];
        }

        DomainEntry? domainEntry = null;
        if (!string.IsNullOrWhiteSpace(domain) && !domain.Equals("general", StringComparison.OrdinalIgnoreCase))
        {
            domainEntry = project.Domains.FirstOrDefault(d => d.DomainId.Equals(domain, StringComparison.OrdinalIgnoreCase));
            if (domainEntry is null)
            {
                errors["domain"] = [$"Unknown domain '{domain}' for project '{project.ProjectId}'."];
            }
        }

        InitiativeEntry? initiativeEntry = null;
        if (!string.IsNullOrWhiteSpace(initiative))
        {
            initiativeEntry = domainEntry?.Initiatives.FirstOrDefault(i => i.InitiativeId.Equals(initiative, StringComparison.OrdinalIgnoreCase));
            if (initiativeEntry is null)
            {
                errors["initiative"] = [domainEntry is null
                    ? "An initiative requires a domain."
                    : $"Initiative '{initiative}' does not belong to domain '{domainEntry.DomainId}'."];
            }
        }

        return new ScopeValidation(errors, project, domainEntry?.DomainId, initiativeEntry?.InitiativeId);
    }
}

public sealed record ScopeValidation(
    IDictionary<string, string[]> Errors,
    ProjectEntry? Project,
    string? Domain,
    string? Initiative)
{
    public bool IsValid => Errors.Count == 0;
}
