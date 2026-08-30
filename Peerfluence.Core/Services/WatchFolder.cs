namespace Peerfluence.Core.Services;

/// <summary>
/// Which files in a watched directory are worth adding, and what becomes of one that has been.
/// </summary>
/// <remarks>
/// Separated from the service that watches the directory so it can be decided without one. What is
/// left is a <see cref="System.IO.FileSystemWatcher"/> and a loop, which is the part that cannot be
/// tested and now has no decisions in it.
/// </remarks>
public static class WatchFolder
{
    /// <summary>What is added to the name of a file that has been dealt with.</summary>
    public const string AddedSuffix = ".added";

    /// <summary>
    /// Whether this file is a torrent that has not already been taken.
    /// </summary>
    public static bool ShouldAdd(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return Path.GetExtension(path).Equals(".torrent", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(path).EndsWith(AddedSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What a file is renamed to once its torrent has been added.
    /// </summary>
    /// <remarks>
    /// Renamed rather than deleted. The directory is somewhere a person drops things, and a torrent
    /// file is small; taking one and then destroying it is a poor trade for the one time somebody
    /// wanted it back. The suffix is also what stops it being added again on the next sweep.
    /// </remarks>
    public static string MarkedPath(string path) => path + AddedSuffix;
}
