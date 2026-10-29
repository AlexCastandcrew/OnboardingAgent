using System.Text.Json;

namespace OnboardingAgent.Core.Chat;

/// <summary>Append-only JSON Lines file. Simple storage for feedback and knowledge gaps.</summary>
public sealed class JsonlStore<T>(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task AppendAsync(T record, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.AppendAllTextAsync(path, JsonSerializer.Serialize(record, Json) + "\n", cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<T>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        return lines.Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<T>(l, Json)!).ToList();
    }
}
