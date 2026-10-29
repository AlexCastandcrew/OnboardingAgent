using OnboardingAgent.Core.Catalog;

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
