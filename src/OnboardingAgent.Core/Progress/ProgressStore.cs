using System.Text.Json;

namespace OnboardingAgent.Core.Progress;

/// <summary>
/// What the server knows about one person's onboarding: the route steps they marked as done, how many questions they
/// asked (a count, never the questions) and when they started and last did something.
/// </summary>
public sealed record UserProgress(
    string UserId,
    string Label,
    bool Sample,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt,
    int QuestionsAsked,
    string? ProjectId,
    string? Role,
    string? Domain,
    string? Initiative,
    IReadOnlyList<string> DoneSteps,
    int TotalSteps);

/// <summary>The route state a client reports for its user.</summary>
public sealed record ProgressUpdate(string ProjectId, string Role, string? Domain, string? Initiative, IReadOnlyList<string> DoneSteps, int TotalSteps);

public static class ProgressMath
{
    public const string Ahead = "ahead";
    public const string OnPace = "onPace";
    public const string Behind = "behind";

    /// <summary>Gap between the share of the route done and the share of the target time elapsed that counts as ahead or behind.</summary>
    private const double PaceTolerance = 0.15;

    /// <summary>1 with nothing done up to 4 with the whole route done.</summary>
    public static int Level(int done, int total) =>
        total <= 0 ? 1 : 1 + (int)Math.Floor(3.0 * Math.Clamp(done, 0, total) / total);

    /// <summary>Day of the onboarding, counting the first day as 1.</summary>
    public static int Day(DateTimeOffset startedAt, DateTimeOffset now) => Math.Max(1, (int)Math.Floor((now - startedAt).TotalDays) + 1);

    /// <summary>Compares the share of the route done with the share of the target days elapsed.</summary>
    public static string Pace(int day, int done, int total, int targetDays)
    {
        if (total <= 0)
        {
            return OnPace;
        }

        var expected = Math.Min(1.0, day / (double)targetDays);
        var actual = Math.Clamp(done, 0, total) / (double)total;
        var gap = actual - expected;
        return gap >= PaceTolerance ? Ahead : gap <= -PaceTolerance ? Behind : OnPace;
    }
}

/// <summary>Per-user progress kept in memory and saved to one JSON file after every change.</summary>
public sealed class ProgressStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, UserProgress> _users = new(StringComparer.Ordinal);

    public ProgressStore(string path, TimeProvider clock)
    {
        _path = path;
        _clock = clock;
        if (File.Exists(path))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<List<UserProgress>>(File.ReadAllText(path), Json) ?? [];
                foreach (var user in saved)
                {
                    _users[user.UserId] = user;
                }
            }
            catch (JsonException)
            {
                // A damaged file must not stop the API: progress starts empty and the next save rewrites it.
            }
        }
    }

    public UserProgress? Get(string userId)
    {
        lock (_gate)
        {
            return _users.GetValueOrDefault(userId);
        }
    }

    public IReadOnlyList<UserProgress> All()
    {
        lock (_gate)
        {
            return _users.Values.OrderBy(u => u.Label, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Stores the route state of a user, creating the record (and its start time) on first use.</summary>
    public UserProgress Save(string userId, string label, ProgressUpdate update)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            var current = _users.GetValueOrDefault(userId);
            var saved = (current ?? New(userId, label, now)) with
            {
                Label = label,
                LastActivityAt = now,
                ProjectId = update.ProjectId,
                Role = update.Role,
                Domain = update.Domain,
                Initiative = update.Initiative,
                DoneSteps = update.DoneSteps.Distinct(StringComparer.Ordinal).ToList(),
                TotalSteps = update.TotalSteps
            };
            _users[userId] = saved;
            Persist();
            return saved;
        }
    }

    /// <summary>Counts one more question for the user. Only the count is kept, never the text.</summary>
    public void RecordQuestion(string userId, string label)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            var current = _users.GetValueOrDefault(userId) ?? New(userId, label, now);
            _users[userId] = current with { Label = label, LastActivityAt = now, QuestionsAsked = current.QuestionsAsked + 1 };
            Persist();
        }
    }

    /// <summary>Removes every sample person and adds the given ones. Real users are never touched.</summary>
    public void ReplaceSampleData(IEnumerable<UserProgress> sample)
    {
        lock (_gate)
        {
            foreach (var id in _users.Values.Where(u => u.Sample).Select(u => u.UserId).ToList())
            {
                _users.Remove(id);
            }

            foreach (var person in sample)
            {
                _users[person.UserId] = person with { Sample = true };
            }

            Persist();
        }
    }

    private static UserProgress New(string userId, string label, DateTimeOffset now) =>
        new(userId, label, false, now, now, 0, null, null, null, null, [], 0);

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_users.Values.ToList(), Json));
        File.Move(temp, _path, overwrite: true);
    }
}

/// <summary>Made-up people for the admin view of a demo. They are labeled sample data wherever they are shown.</summary>
public static class SampleProgress
{
    public static IReadOnlyList<UserProgress> Build(DateTimeOffset now)
    {
        var steps = new[] { "overview", "access", "setup", "domain" };
        (string Team, string Role, int Done, int Day, int HoursSinceActivity)[] rows =
        [
            ("silverwing", "developer", 3, 9, 3),
            ("silverwing", "qa", 2, 10, 20),
            ("silverwing", "leader", 4, 8, 6),
            ("rhaegal", "developer", 1, 12, 52),
            ("rhaegal", "qa", 2, 11, 30),
            ("vhagar", "developer", 2, 7, 5),
            ("vhagar", "developer", 1, 5, 2),
            ("silverwing", "developer", 2, 14, 70)
        ];
        return rows.Select((r, i) => new UserProgress(
            $"sample-{i + 1:00}",
            $"Person {i + 1:00}",
            true,
            now.AddDays(-(r.Day - 1)),
            now.AddHours(-r.HoursSinceActivity),
            r.Done * 3 + i,
            "htg",
            r.Role,
            r.Team,
            null,
            steps.Take(r.Done).ToList(),
            steps.Length)).ToList();
    }
}
