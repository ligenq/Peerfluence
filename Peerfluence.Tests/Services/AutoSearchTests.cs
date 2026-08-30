using Peerfluence.Core.Config;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

/// <summary>
/// Which results of a saved query are new, and how often it may run.
/// </summary>
public sealed class AutoSearchTests
{
    private static TorrentSearchResult Result(string link) =>
        new("A title", 1024, 10, 2, "indexer", null, link);

    [Fact]
    public void ItIsNotRunnable_WithoutBeingTurnedOn()
    {
        Assert.False(AutoSearch.IsRunnable(new AutoSearchSettings { Enabled = false, Query = "something" }));
    }

    [Fact]
    public void ItIsNotRunnable_WithoutAQuery()
    {
        Assert.False(AutoSearch.IsRunnable(new AutoSearchSettings { Enabled = true, Query = "   " }));
    }

    [Fact]
    public void ItIsRunnable_WhenItHasBoth()
    {
        Assert.True(AutoSearch.IsRunnable(new AutoSearchSettings { Enabled = true, Query = "something" }));
    }

    [Fact]
    public void TheIntervalHasAFloor()
    {
        // Somebody else's server's time. A settings file asking for one minute would be a small
        // denial of service performed politely and repeatedly.
        var interval = AutoSearch.Interval(new AutoSearchSettings { IntervalMinutes = 1 });

        Assert.Equal(TimeSpan.FromMinutes(AutoSearch.MinimumIntervalMinutes), interval);
    }

    [Fact]
    public void ALongerIntervalIsHonoured()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(180),
            AutoSearch.Interval(new AutoSearchSettings { IntervalMinutes = 180 }));
    }

    [Fact]
    public void OnlyResultsItHasNotSeenAreNew()
    {
        var settings = new AutoSearchSettings { AlreadyAdded = { "magnet:one" } };

        var results = AutoSearch.NewResults(settings, [Result("magnet:one"), Result("magnet:two")]);

        Assert.Equal(["magnet:two"], results.Select(r => r.Link));
    }

    [Fact]
    public void ALinkIsNewOnlyOnceWithinTheSameBatch()
    {
        // An indexer can return the same release twice; adding it twice is still adding it twice.
        var results = AutoSearch.NewResults(
            new AutoSearchSettings(), [Result("magnet:one"), Result("magnet:one")]);

        Assert.Single(results);
    }

    [Fact]
    public void AResultWithNoLink_IsNotNew()
    {
        Assert.Empty(AutoSearch.NewResults(new AutoSearchSettings(), [Result("  ")]));
    }

    [Fact]
    public void RememberingKeepsTheHistoryFromGrowingForEver()
    {
        var settings = new AutoSearchSettings();

        for (int i = 0; i < AutoSearch.HistoryLimit + 50; i++)
        {
            AutoSearch.Remember(settings, $"magnet:{i}");
        }

        Assert.Equal(AutoSearch.HistoryLimit, settings.AlreadyAdded.Count);

        // The oldest go first, so what is remembered is what was seen most recently.
        Assert.DoesNotContain("magnet:0", settings.AlreadyAdded);
        Assert.Contains($"magnet:{AutoSearch.HistoryLimit + 49}", settings.AlreadyAdded);
    }

    [Fact]
    public void RememberingNothing_ChangesNothing()
    {
        var settings = new AutoSearchSettings();

        AutoSearch.Remember(settings, "   ");

        Assert.Empty(settings.AlreadyAdded);
    }
}
