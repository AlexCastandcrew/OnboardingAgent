using OnboardingAgent.Core.Options;
using OnboardingAgent.Core.Progress;

namespace OnboardingAgent.Core.UnitTests;

public class ProgressTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "onboarding-progress-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Cleaned by the OS eventually.
        }
    }

    [Theory]
    [InlineData(0, 4, 1)]
    [InlineData(1, 4, 1)]
    [InlineData(2, 4, 2)]
    [InlineData(3, 4, 3)]
    [InlineData(4, 4, 4)]
    [InlineData(3, 3, 4)]
    [InlineData(0, 0, 1)]
    [InlineData(9, 4, 4)]
    public void Level_ShareOfRouteDone_IsOneToFour(int done, int total, int expected) =>
        Assert.Equal(expected, ProgressMath.Level(done, total));

    [Theory]
    [InlineData(7, 4, 4, 14, "ahead")]
    [InlineData(7, 2, 4, 14, "onPace")]
    [InlineData(14, 2, 4, 14, "behind")]
    [InlineData(20, 4, 4, 14, "onPace")]
    [InlineData(1, 0, 4, 14, "onPace")]
    [InlineData(5, 0, 0, 14, "onPace")]
    public void Pace_ProgressAgainstTargetDays_IsAheadOnPaceOrBehind(int day, int done, int total, int target, string expected)
    {
        Assert.Equal(expected, ProgressMath.Pace(day, done, total, target));
    }

    [Fact]
    public void Day_FirstDayIsOne()
    {
        var start = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

        Assert.Equal(1, ProgressMath.Day(start, start.AddHours(5)));
        Assert.Equal(3, ProgressMath.Day(start, start.AddDays(2).AddHours(1)));
    }

    [Fact]
    public void Save_NewUser_KeepsStartTimeAndCountsAcrossLaterSaves()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var store = new ProgressStore(Path.Combine(_folder, "progress.json"), clock);
        var update = new ProgressUpdate("htg", "developer", "silverwing", null, ["overview"], 4);

        store.Save("ana", "ana", update);
        store.RecordQuestion("ana", "ana");
        clock.Advance(TimeSpan.FromDays(3));
        var saved = store.Save("ana", "ana", update with { DoneSteps = ["overview", "access", "access"] });

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), saved.StartedAt);
        Assert.Equal(1, saved.QuestionsAsked);
        Assert.Equal(["overview", "access"], saved.DoneSteps);
    }

    [Fact]
    public void Store_Reloaded_KeepsWhatWasSaved()
    {
        var path = Path.Combine(_folder, "progress.json");
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        new ProgressStore(path, clock).Save("ana", "Ana", new ProgressUpdate("htg", "qa", null, null, ["overview"], 3));

        var reloaded = new ProgressStore(path, clock).Get("ana");

        Assert.NotNull(reloaded);
        Assert.Equal("qa", reloaded.Role);
        Assert.Equal(["overview"], reloaded.DoneSteps);
    }

    [Fact]
    public void Store_DamagedFile_StartsEmptyInsteadOfFailing()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "progress.json");
        File.WriteAllText(path, "{ not json");

        var store = new ProgressStore(path, TimeProvider.System);

        Assert.Empty(store.All());
    }

    [Fact]
    public void ReplaceSampleData_ReplacesOnlySamplePeople()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new ProgressStore(Path.Combine(_folder, "progress.json"), clock);
        store.Save("ana", "Ana", new ProgressUpdate("htg", "developer", null, null, [], 4));

        store.ReplaceSampleData(SampleProgress.Build(clock.GetUtcNow()));
        store.ReplaceSampleData(SampleProgress.Build(clock.GetUtcNow()));

        var all = store.All();
        Assert.Equal(8, all.Count(u => u.Sample));
        Assert.Single(all, u => !u.Sample);
    }

    [Fact]
    public void ProgressOptions_TargetDaysBelowOne_IsReported()
    {
        Assert.Empty(new ProgressOptions().Validate());
        Assert.Single(new ProgressOptions { TargetDays = 0 }.Validate());
    }
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}