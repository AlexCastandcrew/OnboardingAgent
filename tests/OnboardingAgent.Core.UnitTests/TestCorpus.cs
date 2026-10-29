using System.Text.Json;
using OnboardingAgent.Core.Ingestion;
using OnboardingAgent.Core.Options;
using OnboardingAgent.Core.Retrieval;

namespace OnboardingAgent.Core.UnitTests;

/// <summary>A temporary corpus folder and index, deleted after the test.</summary>
internal sealed class TestCorpus : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "onboarding-tests", Guid.NewGuid().ToString("N"));
    private readonly List<ManifestDocument> _documents = [];

    public TestCorpus()
    {
        Directory.CreateDirectory(Folder);
        Index = new KnowledgeIndex(Path.Combine(_root, "index.db"));
        Options = new CorpusOptions { ExcludedPatterns = ["appsettings*", ".env*"] };
        Ingestion = new IngestionService(Index, Options, TimeProvider.System);
    }

    public string Folder => Path.Combine(_root, "corpus");
    public string ManifestPath => Path.Combine(Folder, "manifest.json");
    public KnowledgeIndex Index { get; }
    public CorpusOptions Options { get; }
    public IngestionService Ingestion { get; }

    public TestCorpus Add(string documentId, string markdown, string[]? domains = null, string[]? initiatives = null, string? path = null, string? projectId = null)
    {
        path ??= documentId + ".md";
        File.WriteAllText(Path.Combine(Folder, path), markdown);
        _documents.RemoveAll(d => d.DocumentId == documentId);
        _documents.Add(new ManifestDocument
        {
            DocumentId = documentId,
            ProjectId = projectId,
            Path = path,
            Title = documentId,
            SourceType = "synthetic",
            Domains = [.. domains ?? ["general"]],
            Initiatives = [.. initiatives ?? []]
        });
        return this;
    }

    public TestCorpus Drop(string documentId)
    {
        _documents.RemoveAll(d => d.DocumentId == documentId);
        return this;
    }

    public Core.Contracts.IngestionResult Ingest()
    {
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(new CorpusManifest { ProjectId = "htg", Documents = _documents }));
        return Ingestion.Ingest(Folder, ManifestPath);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // The temp folder is cleaned by the OS eventually.
        }
    }
}
