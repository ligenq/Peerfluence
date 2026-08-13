using System;
using Peerfluence.Core.Services;
using Peerfluence.Properties;

namespace Peerfluence.ViewModels;

/// <summary>
/// One search result, as the grid shows it. Read-only: a result is a snapshot of what an indexer
/// said, and nothing updates it afterwards.
/// </summary>
public sealed class TorrentSearchResultViewModel
{
    private readonly TorrentSearchResult _result;

    public TorrentSearchResultViewModel(TorrentSearchResult result)
    {
        _result = result;
    }

    public string Title => _result.Title;

    public long SizeBytes => _result.SizeBytes;

    public int Seeders => _result.Seeders;

    public int Peers => _result.Peers;

    public string IndexerName => _result.IndexerName;

    public string Link => _result.Link;

    public bool IsMagnet => _result.IsMagnet;

    /// <summary>
    /// A dash where a count is missing, so the column does not claim a zero the indexer never said.
    /// </summary>
    public string SeedersText => Format(_result.Seeders);

    public string PeersText => Format(_result.Peers);

    /// <summary>
    /// How long ago it was posted, in the roughest useful unit. Exact timestamps are noise in a
    /// list being skimmed for something to download.
    /// </summary>
    public string AgeText
    {
        get
        {
            if (_result.PublishedAt is not { } published)
            {
                return "—";
            }

            var age = DateTimeOffset.UtcNow - published;
            if (age < TimeSpan.Zero)
            {
                return "—";
            }

            if (age.TotalDays >= 365)
            {
                return string.Format(Resources.Find_AgeYears, (int)(age.TotalDays / 365));
            }

            if (age.TotalDays >= 7)
            {
                return string.Format(Resources.Find_AgeWeeks, (int)(age.TotalDays / 7));
            }

            if (age.TotalDays >= 1)
            {
                return string.Format(Resources.Find_AgeDays, (int)age.TotalDays);
            }

            return string.Format(Resources.Find_AgeHours, Math.Max(1, (int)age.TotalHours));
        }
    }

    private static string Format(int value)
    {
        return value < 0 ? "—" : value.ToString("N0");
    }
}
