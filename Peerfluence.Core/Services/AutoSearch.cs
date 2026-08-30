using Peerfluence.Core.Config;

namespace Peerfluence.Core.Services;

/// <summary>
/// Which of a query's results are new, and how much history is worth keeping.
/// </summary>
/// <remarks>
/// The decisions of the automatic search, separated from the clock and the network so they can be
/// made in a test. What is left in the service is a timer and two calls.
/// </remarks>
public static class AutoSearch
{
    /// <summary>The shortest interval that will be honoured, whatever the settings say.</summary>
    public const int MinimumIntervalMinutes = 15;

    /// <summary>
    /// How many links to remember. Enough that a result cannot reappear before it has fallen off
    /// the indexer's own first page, and small enough that the settings file stays a settings file.
    /// </summary>
    public const int HistoryLimit = 500;

    /// <summary>
    /// Whether there is anything to run.
    /// </summary>
    public static bool IsRunnable(AutoSearchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Enabled && !string.IsNullOrWhiteSpace(settings.Query);
    }

    /// <summary>
    /// How long to wait between runs, never less than <see cref="MinimumIntervalMinutes"/>.
    /// </summary>
    /// <remarks>
    /// A floor rather than trust: the interval is somebody else's server's time, and a settings file
    /// asking for one minute would be a small denial of service performed politely and repeatedly.
    /// </remarks>
    public static TimeSpan Interval(AutoSearchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return TimeSpan.FromMinutes(Math.Max(MinimumIntervalMinutes, settings.IntervalMinutes));
    }

    /// <summary>
    /// The results that have not been acted on before.
    /// </summary>
    public static IReadOnlyList<TorrentSearchResult> NewResults(
        AutoSearchSettings settings,
        IEnumerable<TorrentSearchResult> results)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(results);

        var seen = new HashSet<string>(settings.AlreadyAdded, StringComparer.OrdinalIgnoreCase);

        return results
            .Where(result => !string.IsNullOrWhiteSpace(result.Link) && seen.Add(result.Link))
            .ToList();
    }

    /// <summary>
    /// Records that a link has been acted on, forgetting the oldest once there are too many.
    /// </summary>
    public static void Remember(AutoSearchSettings settings, string link)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(link))
        {
            return;
        }

        settings.AlreadyAdded.Add(link);

        if (settings.AlreadyAdded.Count > HistoryLimit)
        {
            settings.AlreadyAdded.RemoveRange(0, settings.AlreadyAdded.Count - HistoryLimit);
        }
    }
}
