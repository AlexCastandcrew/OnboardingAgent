using OnboardingAgent.Core.Options;

namespace OnboardingAgent.Core.UnitTests;

public class OptionsValidationTests
{
    [Fact]
    public void Validate_Defaults_ReportNothing()
    {
        Assert.Empty(new CorpusOptions().Validate());
        Assert.Empty(new ChatOptions().Validate());
    }

    [Fact]
    public void Validate_SeveralBadCorpusSettings_ReportsEveryOne()
    {
        var options = new CorpusOptions { Folder = " ", ChunkTargetChars = 500, ChunkOverlapChars = 500 };

        var problems = options.Validate().ToList();

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("Corpus:Folder", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("ChunkOverlapChars (500)") && p.Contains("ChunkTargetChars (500)"));
    }

    [Fact]
    public void Validate_MoreContextChunksThanCandidates_IsReported()
    {
        var problem = Assert.Single(new ChatOptions { CandidateCount = 4, ContextChunks = 6 }.Validate());

        Assert.Contains("CandidateCount (4)", problem);
    }

    [Fact]
    public void Validate_PathsWithMissingManifestAndDataFiles_ReportsEachFile()
    {
        var root = Directory.CreateTempSubdirectory("onboarding-paths").FullName;
        try
        {
            var paths = CorpusPaths.Resolve(new CorpusOptions { Folder = root, DataFolder = root, StateFolder = root }, root);

            var problems = paths.Validate().ToList();

            Assert.Equal(3, problems.Count);
            Assert.Contains(problems, p => p.Contains("manifest.json"));
            Assert.Contains(problems, p => p.Contains("projects.json"));
            Assert.Contains(problems, p => p.Contains("onboarding.json"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
