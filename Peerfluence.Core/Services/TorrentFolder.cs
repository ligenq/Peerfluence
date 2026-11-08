using System.Text.RegularExpressions;

namespace Peerfluence.Core.Services;

/// <summary>
/// The folder a torrent gets of its own. Its name is chosen by whoever made the torrent, so it is not
/// to be trusted as a path: the engine checks a torrent's files against the folder it is given, and a
/// name that moves the folder would take those checks along with it.
/// </summary>
public static partial class TorrentFolder
{
    private const string Fallback = "torrent";

    /// <summary>A folder directly inside <paramref name="root"/>, whatever the name says.</summary>
    public static string Within(string root, string torrentName) =>
        Path.Combine(root, FolderName(torrentName));

    private static string FolderName(string torrentName)
    {
        // Both separators and every character Windows refuses, on every system: a download folder can
        // be on a shared drive, and the same torrent should land in the same place.
        char[] characters = torrentName.ToCharArray();
        for (int i = 0; i < characters.Length; i++)
        {
            if (characters[i] is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(characters[i]))
            {
                characters[i] = '_';
            }
        }

        // Windows drops trailing dots and spaces, so "name." is "name", and ".." has no name at all.
        string name = new string(characters).Trim().TrimEnd('.', ' ');
        if (name.Length == 0)
        {
            return Fallback;
        }

        return WindowsDeviceName().IsMatch(name) ? "_" + name : name;
    }

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM\d|LPT\d)(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsDeviceName();
}
