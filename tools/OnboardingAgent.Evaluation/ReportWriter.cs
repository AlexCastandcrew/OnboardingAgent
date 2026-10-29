using System.Text;

namespace OnboardingAgent.Evaluation;

/// <summary>Markdown report: gates per run, then each case with what the manual review must check.</summary>
public static class ReportWriter
{
    public static string Markdown(EvalReport report)
    {
        var text = new StringBuilder();
        text.AppendLine($"# Evaluation {report.StartedAt:yyyy-MM-dd HH:mm} UTC").AppendLine();
        text.AppendLine($"API: {report.BaseUrl}. Runs: {report.Runs.Count}. {Mark(report.ConsecutiveRuns.Passed)} {report.ConsecutiveRuns.Name}: {report.ConsecutiveRuns.Detail}");
        text.AppendLine();
        text.AppendLine("Automatic checks only. Whether each answer states the expected facts is decided in the manual review below.");

        foreach (var run in report.Runs)
        {
            text.AppendLine().AppendLine($"## Run {run.Run}: {(run.Passed ? "all gates pass" : "gates failing")}").AppendLine();
            text.AppendLine($"Median {run.MedianMs} ms, max {run.MaxMs} ms.").AppendLine();
            text.AppendLine("| Gate | Result | Detail |").AppendLine("|---|---|---|");
            foreach (var gate in run.Gates)
            {
                text.AppendLine($"| {gate.Name} | {Mark(gate.Passed)} | {Cell(gate.Detail)} |");
            }

            text.AppendLine().AppendLine("| Case | Category | Result | Status | Cited | ms | Reasons |").AppendLine("|---|---|---|---|---|---:|---|");
            foreach (var result in run.Cases)
            {
                var reasons = string.Join(" ", result.Reasons) + (result.NeedsCorpus && !result.Passed ? " (needs corpus T03)" : "");
                text.AppendLine($"| {result.CaseId} | {result.Category} | {Mark(result.Passed)} | {result.EvidenceStatus ?? "-"} | {Cell(string.Join(", ", result.CitedDocumentIds))} | {result.ElapsedMs} | {Cell(reasons)} |");
            }
        }

        var last = report.Runs.LastOrDefault();
        if (last is not null)
        {
            text.AppendLine().AppendLine($"## Manual review (run {last.Run})").AppendLine();
            foreach (var result in last.Cases)
            {
                text.AppendLine($"### {result.CaseId}").AppendLine();
                text.AppendLine("Expected facts:");
                foreach (var fact in result.ExpectedFacts)
                {
                    text.AppendLine($"- [ ] {fact}");
                }

                text.AppendLine().AppendLine("Answer:").AppendLine();
                text.AppendLine("> " + (result.Answer ?? "(no answer)").Replace("\n", "\n> ")).AppendLine();
            }
        }

        return text.ToString();
    }

    private static string Mark(bool passed) => passed ? "PASS" : "FAIL";

    private static string Cell(string value) => value.Replace("|", "\\|").Replace("\n", " ");
}
