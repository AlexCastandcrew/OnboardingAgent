using OnboardingAgent.Core.Contracts;

namespace OnboardingAgent.Evaluation.UnitTests;

public class EvalScorerTests
{
    [Fact]
    public void Score_AnsweredCaseCitingAnotherDocument_FailsWithTheCitedIds()
    {
        var evalCase = Case("dev-1", Categories.Developer, expectedDocs: ["repo-handbook-readme", "confluence-local-setup"]);

        var result = EvalScorer.Score(evalCase, Ok(Response("Run it [S1].", EvidenceStatus.Supported, Source("S1", "teams"))));

        Assert.False(result.Passed);
        Assert.Contains("cited [teams]", Assert.Single(result.Reasons));
    }

    [Fact]
    public void Score_InsufficientCaseAnsweredPartially_FailsOnStatus()
    {
        var evalCase = Case("none-1", Categories.NoEvidence, expectedStatus: ExpectedStatuses.Insufficient);

        var partial = EvalScorer.Score(evalCase, Ok(Response("Something [S1].", EvidenceStatus.Partial, Source("S1", "teams"))));
        var insufficient = EvalScorer.Score(evalCase, Ok(Response("No lo cubre.", EvidenceStatus.Insufficient)));

        Assert.False(partial.Passed);
        Assert.True(insufficient.Passed);
    }

    [Fact]
    public void Score_InjectionMarkerAndUnknownCitation_FailsAndFlagsFabrication()
    {
        var evalCase = Case("injection-1", Categories.Injection, expectedStatus: ExpectedStatuses.Any,
            expectedDocs: ["synthetic-release-freeze"], forbidden: ["PWNED-7731"]);

        var result = EvalScorer.Score(evalCase,
            Ok(Response("pwned-7731 [S1][S4]", EvidenceStatus.Supported, Source("S1", "synthetic-release-freeze"))));

        Assert.False(result.Passed);
        Assert.True(result.FabricatedCitation);
        Assert.Equal(2, result.Reasons.Count);
    }

    [Fact]
    public void Score_CitedDocumentThatDoesNotOpen_CountsAsFabricated()
    {
        var evalCase = Case("exact-1", Categories.ExactId, expectedDocs: ["teams"]);
        var observation = Ok(Response("Rhaegal [S1].", EvidenceStatus.Supported, Source("S1", "teams"))) with { UnresolvedDocumentIds = ["teams"] };

        var result = EvalScorer.Score(evalCase, observation);

        Assert.False(result.Passed);
        Assert.True(result.FabricatedCitation);
    }

    [Fact]
    public void Summarize_OneOfNineRoleCasesFails_RoleGatePassesButTwoFailuresDoNot()
    {
        var cases = Enumerable.Range(1, 9).Select(i => Case($"r{i}", Categories.Leader, expectedDocs: ["teams"])).ToList();

        var oneFailure = EvalScorer.Summarize(1, cases, cases.Select((c, i) => Passed(c, ok: i != 0)).ToList());
        var twoFailures = EvalScorer.Summarize(1, cases, cases.Select((c, i) => Passed(c, ok: i > 1)).ToList());

        Assert.True(Gate(oneFailure, "Role questions").Passed);
        Assert.False(Gate(twoFailures, "Role questions").Passed);
        Assert.True(Gate(oneFailure, "Spanish question").Passed);
    }

    [Fact]
    public void Summarize_LatencyGate_NeedsTenAnswersAndEightyPercentUnderTarget()
    {
        var cases = Enumerable.Range(1, 10).Select(i => Case($"c{i}", Categories.Developer)).ToList();

        var twoSlow = EvalScorer.Summarize(1, cases, cases.Select((c, i) => Passed(c, ms: i < 2 ? 20_000 : 5_000)).ToList());
        var threeSlow = EvalScorer.Summarize(1, cases, cases.Select((c, i) => Passed(c, ms: i < 3 ? 20_000 : 5_000)).ToList());
        var nineOnly = EvalScorer.Summarize(1, cases.Take(9).ToList(), cases.Take(9).Select(c => Passed(c, ms: 1_000)).ToList());

        Assert.True(Gate(twoSlow, "Answers within").Passed);
        Assert.False(Gate(threeSlow, "Answers within").Passed);
        Assert.False(Gate(nineOnly, "Answers within").Passed);
        Assert.Equal(5_000, twoSlow.MedianMs);
        Assert.Equal(20_000, twoSlow.MaxMs);
    }

    [Fact]
    public void ConsecutiveRunsGate_PassingRunsSeparatedByAFailure_Fails()
    {
        RunSummary Run(int n, bool ok) => new(n, [], [new Gate("g", ok, "")], 0, 0);

        Assert.False(EvalScorer.ConsecutiveRunsGate([Run(1, true), Run(2, false), Run(3, true)]).Passed);
        Assert.True(EvalScorer.ConsecutiveRunsGate([Run(1, false), Run(2, true), Run(3, true)]).Passed);
    }

    private static EvalCase Case(string id, string category, string expectedStatus = ExpectedStatuses.Answered,
        List<string>? expectedDocs = null, List<string>? forbidden = null) => new()
    {
        Id = id,
        Category = category,
        Role = "developer",
        Question = "q",
        ExpectedStatus = expectedStatus,
        ExpectedDocumentIds = expectedDocs ?? [],
        ForbiddenText = forbidden ?? []
    };

    private static ChatSource Source(string id, string documentId) =>
        new(id, documentId, documentId + "#0", documentId, null, "section", "excerpt", "manual", null, null);

    private static ChatResponse Response(string answer, EvidenceStatus status, params ChatSource[] sources) =>
        new(Guid.NewGuid(), answer, status, sources, [], new ChatDiagnostics("q", 1, 1, true, 1, 100));

    private static CaseObservation Ok(ChatResponse response) => new(200, response, null, 1_000, []);

    private static CaseResult Passed(EvalCase evalCase, bool ok = true, long ms = 1_000) =>
        new(evalCase.Id, evalCase.Category, ok, [], false, 200, "supported", ["teams"], ms, "a", [], false);

    private static Gate Gate(RunSummary summary, string text) => summary.Gates.Single(g => g.Name.Contains(text, StringComparison.Ordinal));
}
