namespace Peerfluence.Core.Config;

/// <summary>
/// A user-defined grouping for downloads, with somewhere for them to land.
/// </summary>
/// <param name="Name">
/// What the user calls it, and the key everything else refers to it by. Categories are few and named
/// by hand, so the name is identity enough and there is no identifier to keep in step with it.
/// </param>
/// <param name="SavePath">
/// Where torrents in this category are saved, or empty to use the ordinary download path. The reason
/// most people want categories at all: films in one place, work in another, without choosing a folder
/// by hand every time.
/// </param>
public sealed record TorrentCategory(string Name, string SavePath = "")
{
    public bool HasSavePath => !string.IsNullOrWhiteSpace(SavePath);
}

/// <summary>
/// Categories, and which torrent is in which.
///
/// <para>
/// Kept here rather than in the engine because the engine has no notion of a label - a torrent to it
/// is an info hash and a set of files. That makes the assignments this application's to hold, and its
/// to tidy up: a torrent removed elsewhere would otherwise leave its category behind forever.
/// </para>
/// </summary>
public sealed class CategorySettings
{
    public List<TorrentCategory> Categories { get; set; } = [];

    /// <summary>
    /// Info hash to category name. A plain dictionary because it is written once per assignment and
    /// read once per row, and because it survives a round trip through the settings file unchanged.
    /// </summary>
    public Dictionary<string, string> Assignments { get; set; } = [];
}
