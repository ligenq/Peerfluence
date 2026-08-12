namespace Peerfluence.ViewModels;

/// <summary>
/// Which torrents the downloads list is showing.
/// </summary>
public enum TorrentFilter
{
    All,

    /// <summary>Running and not yet complete.</summary>
    Downloading,

    /// <summary>Complete and still running, so still uploading.</summary>
    Seeding,

    /// <summary>Complete, whether or not it is still running.</summary>
    Completed
}
