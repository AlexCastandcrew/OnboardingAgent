using OnboardingAgent.Core.Catalog;
using OnboardingAgent.Core.Retrieval;

namespace OnboardingAgent.Core.UnitTests;

public class OnboardingAndCatalogTests
{
    [Fact]
    public void Build_InitiativeAndRoleRoutes_TakeTheMostSpecificStepAndKeepGeneralOnes()
    {
        using var corpus = new TestCorpus().Add("teams", "# Teams\n\nText.").Add("excel", "# Excel\n\nText.");
        corpus.Ingest();
        var file = new OnboardingFile
        {
            Routes =
            [
                Route("*", null, null, Step(1, "general overview", "teams"), Step(2, "access", "access-doc"), Step(3, "general setup", "teams"), Step(4, "general q", "teams")),
                Route("qa", null, null, Step(3, "qa setup", "teams")),
                Route("*", "silverwing", "H2G-6893", Step(1, "excel overview", "excel"), Step(3, "excel setup", "excel")),
                Route("qa", "silverwing", "H2G-6893", Step(4, "excel qa questions", "excel")),
                Route("developer", "silverwing", "H2G-6893", Step(4, "excel dev questions", "excel"))
            ]
        };

        var route = new OnboardingService(file, corpus.Index).Build("htg", "qa", "silverwing", "H2G-6893");

        Assert.Equal(["excel overview", "access", "excel setup", "excel qa questions"], route.Steps.Select(s => s.Title));
        // The access document is not in the corpus, so the step is pending instead of invented.
        Assert.Equal(["ready", "pending", "ready", "ready"], route.Steps.Select(s => s.Status));
        Assert.Empty(route.Steps[1].DocumentIds);
    }

    [Fact]
    public void Build_EnglishRequested_UsesEnglishTextsAndFallsBackToSpanishPerField()
    {
        using var corpus = new TestCorpus().Add("teams", "# Teams\n\nText.");
        corpus.Ingest();
        var translated = Step(1, "Resumen", "teams");
        translated.Goal = "Meta";
        translated.SuggestedQuestions = ["¿Qué es esto?"];
        translated.En = new StepTexts { Title = "Overview", SuggestedQuestions = ["What is this?"] };
        var file = new OnboardingFile { Routes = [Route("*", null, null, translated, Step(2, "Accesos", "teams"))] };
        var service = new OnboardingService(file, corpus.Index);

        var english = service.Build("htg", "qa", null, null, "en");
        var spanish = service.Build("htg", "qa", null, null, null);

        Assert.Equal(("Overview", "Meta", "What is this?"), (english.Steps[0].Title, english.Steps[0].Goal, english.Steps[0].SuggestedQuestions[0]));
        Assert.Equal("Accesos", english.Steps[1].Title);
        Assert.Equal(("Resumen", "¿Qué es esto?"), (spanish.Steps[0].Title, spanish.Steps[0].SuggestedQuestions[0]));
    }

    [Fact]
    public void Domains_EnglishRequested_UsesTheEnglishInitiativeNameWhenThereIsOne()
    {
        var project = new ProjectEntry
        {
            ProjectId = "htg",
            Domains =
            [
                new DomainEntry
                {
                    DomainId = "silverwing",
                    Initiatives =
                    [
                        new InitiativeEntry { InitiativeId = "H2G-6627", Name = "Fase 1", NameEn = "Phase 1" },
                        new InitiativeEntry { InitiativeId = "H2G-6893", Name = "Excel Import" }
                    ]
                }
            ]
        };
        var catalog = new ProjectCatalog(new CatalogFile { Projects = [project] });

        var english = catalog.Domains(project, "en").Single().Initiatives.Select(i => i.Name);
        var spanish = catalog.Domains(project, "es").Single().Initiatives.Select(i => i.Name);

        Assert.Equal(["Phase 1", "Excel Import"], english);
        Assert.Equal(["Fase 1", "Excel Import"], spanish);
    }

    [Fact]
    public void Build_IndexedDocuments_CountsSourceTypesAndRepositoriesFromTheirUrls()
    {
        var project = new ProjectEntry
        {
            ProjectId = "htg",
            Domains = [new DomainEntry { DomainId = "a", Initiatives = [new InitiativeEntry { InitiativeId = "X-1" }, new InitiativeEntry { InitiativeId = "X-2" }] }, new DomainEntry { DomainId = "b" }]
        };
        var documents = new[]
        {
            Document("readme", "repo", "https://github.com/acme/htg/blob/abc/README.md", "abc", "2026-10-28T10:00:00Z"),
            Document("design", "repo", "https://github.com/acme/htg/blob/abc/docs/design.md", "abc", "2026-10-29T10:00:00Z"),
            Document("elsewhere", "repo", "https://example.com/a.md", "v1", "2026-10-27T10:00:00Z"),
            Document("wiki", "confluence", "https://acme.atlassian.net/wiki/spaces/HPS/pages/1", "snap", "2026-10-26T10:00:00Z"),
            Document("ticket", "jira", null, null, "2026-10-25T10:00:00Z")
        };
        var passages = new Dictionary<string, int> { ["readme"] = 4, ["design"] = 6, ["elsewhere"] = 1, ["wiki"] = 3, ["ticket"] = 2 };

        var summary = KnowledgeSummaryBuilder.Build(project, documents, passages);

        Assert.Equal((5, 16, "2026-10-29T10:00:00Z", 2, 2), (summary.Documents, summary.Passages, summary.LastIndexedAt, summary.Teams, summary.Initiatives));
        Assert.Equal(["repo:3:11", "confluence:1:3", "jira:1:2"], summary.SourceTypes.Select(s => $"{s.SourceType}:{s.Documents}:{s.Passages}"));
        var repository = Assert.Single(summary.Repositories);
        Assert.Equal(("acme/htg", "https://github.com/acme/htg", 2, "abc"), (repository.Name, repository.Url, repository.Documents, repository.Version));
    }

    [Fact]
    public void Build_NothingIndexed_ReturnsZerosAndNoLastIndexedDate()
    {
        var summary = KnowledgeSummaryBuilder.Build(new ProjectEntry { ProjectId = "caps" }, [], new Dictionary<string, int>());

        Assert.Equal((0, 0, null), (summary.Documents, summary.Passages, summary.LastIndexedAt));
        Assert.Empty(summary.SourceTypes);
        Assert.Empty(summary.Repositories);
    }

    private static IndexedDocument Document(string id, string sourceType, string? url, string? version, string ingestedAt) =>
        new(id, "htg", id, id + ".md", sourceType, url, version, null, ingestedAt, "checksum", ["general"], [], "");

    [Fact]
    public void Validate_InitiativeFromAnotherDomain_IsRejected()
    {
        var catalog = new ProjectCatalog(new CatalogFile
        {
            Projects =
            [
                new ProjectEntry
                {
                    ProjectId = "htg",
                    Domains =
                    [
                        new DomainEntry { DomainId = "silverwing", Initiatives = [new InitiativeEntry { InitiativeId = "H2G-6893" }] },
                        new DomainEntry { DomainId = "vhagar", Initiatives = [new InitiativeEntry { InitiativeId = "H2G-5928" }] }
                    ]
                }
            ]
        });

        var wrongDomain = catalog.Validate("htg", "qa", "vhagar", "H2G-6893");
        var noDomain = catalog.Validate("htg", "qa", null, "H2G-6893");
        var valid = catalog.Validate("HTG", "qa", "SilverWing", "h2g-6893");

        Assert.Contains("initiative", wrongDomain.Errors.Keys);
        Assert.Contains("initiative", noDomain.Errors.Keys);
        Assert.True(valid.IsValid);
        Assert.Equal(("silverwing", "H2G-6893"), (valid.Domain, valid.Initiative));
    }

    private static RouteEntry Route(string role, string? domain, string? initiative, params StepEntry[] steps) =>
        new() { ProjectId = "htg", Role = role, Domain = domain, Initiative = initiative, Steps = [.. steps] };

    private static StepEntry Step(int number, string title, string documentId) =>
        new() { Step = number, Key = $"k{number}", Title = title, DocumentIds = [documentId] };
}
