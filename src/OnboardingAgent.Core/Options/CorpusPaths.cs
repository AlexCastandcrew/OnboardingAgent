namespace OnboardingAgent.Core.Options;

/// <summary>Absolute paths resolved from <see cref="CorpusOptions"/> against the content root.</summary>
public sealed record CorpusPaths(string CorpusFolder, string ManifestPath, string DataFolder, string StateFolder)
{
    public static CorpusPaths Resolve(CorpusOptions options, string contentRoot)
    {
        string Full(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(contentRoot, path));
        var corpus = Full(options.Folder);
        return new CorpusPaths(corpus, Path.Combine(corpus, options.ManifestFile), Full(options.DataFolder), Full(options.StateFolder));
    }

    /// <summary>The folders and files the API reads at startup that are missing.</summary>
    public IEnumerable<string> Validate()
    {
        if (!Directory.Exists(CorpusFolder))
        {
            yield return $"Corpus folder '{CorpusFolder}' does not exist (Corpus:Folder).";
        }
        else if (!File.Exists(ManifestPath))
        {
            yield return $"Manifest '{ManifestPath}' does not exist (Corpus:ManifestFile).";
        }

        foreach (var file in new[] { "projects.json", "onboarding.json" })
        {
            var path = Path.Combine(DataFolder, file);
            if (!File.Exists(path))
            {
                yield return $"'{path}' does not exist (Corpus:DataFolder).";
            }
        }
    }
}
