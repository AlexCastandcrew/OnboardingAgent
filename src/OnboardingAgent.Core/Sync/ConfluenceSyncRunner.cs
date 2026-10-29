using OnboardingAgent.Core.Contracts;
using OnboardingAgent.Core.Ingestion;
using OnboardingAgent.Core.Options;

namespace OnboardingAgent.Core.Sync;

/// <summary>What the sync trigger returns. <see cref="Ingestion"/> is null when no page changed, so there was nothing to ingest.</summary>
public sealed record ConfluenceSyncReport(ConfluenceSyncResult Sync, IngestionResult? Ingestion);

/// <summary>Runs the sync and, when pages changed, the usual ingestion (checksums skip unchanged documents).</summary>
public sealed class ConfluenceSyncRunner(ConfluenceSyncService sync, IngestionService ingestion, CorpusPaths paths)
{
    public async Task<ConfluenceSyncReport> RunAsync(CancellationToken cancellationToken)
    {
        var result = await sync.SyncAsync(cancellationToken);
        var ingested = result.Synced.Count > 0 ? ingestion.Ingest(paths.CorpusFolder, paths.ManifestPath) : null;
        return new ConfluenceSyncReport(result, ingested);
    }
}
