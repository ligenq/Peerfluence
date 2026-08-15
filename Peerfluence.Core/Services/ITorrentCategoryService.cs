using Peerfluence.Core.Config;
using PeerSharp.Core;

namespace Peerfluence.Core.Services;

/// <summary>
/// Keeps the categories, and remembers which torrent is in which.
/// </summary>
public interface ITorrentCategoryService
{
    /// <summary>The categories as defined, in the order they were added.</summary>
    IReadOnlyList<TorrentCategory> Categories { get; }

    /// <summary>
    /// The category a torrent is in, or null. Null for a torrent nobody has filed, which is most of
    /// them - being in no category is the ordinary state, not an error.
    /// </summary>
    string? GetCategory(InfoHash hash);

    /// <summary>
    /// Files a torrent, or takes it out of every category when given null. Saves, because an
    /// assignment the user made and then lost on restart would be worse than not offering this.
    /// </summary>
    Task AssignAsync(InfoHash hash, string? categoryName, CancellationToken cancellationToken = default);

    /// <summary>Where a category saves to, or null when it has no path of its own.</summary>
    string? ResolveSavePath(string? categoryName);

    Task AddAsync(string name, string savePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a category, and unfiles everything that was in it. Leaving assignments pointing at a
    /// category that no longer exists would show torrents filed under a name with no meaning.
    /// </summary>
    Task RemoveAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets assignments for torrents that are no longer here.
    ///
    /// <para>
    /// Torrents are removed from several places, and threading a callback through all of them to
    /// delete one dictionary entry is more machinery than the problem deserves. Sweeping on startup
    /// keeps the file from growing forever without any of that.
    /// </para>
    /// </summary>
    Task ForgetMissingAsync(IEnumerable<InfoHash> present, CancellationToken cancellationToken = default);
}
