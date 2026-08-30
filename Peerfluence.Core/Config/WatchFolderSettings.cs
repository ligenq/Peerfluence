namespace Peerfluence.Core.Config;

/// <summary>
/// A directory that torrent files can be dropped into to be added.
/// </summary>
/// <remarks>
/// The other half of a loop the application already had: <c>CompletionActionSettings</c> runs
/// something when a download finishes, and this is how one starts without anybody opening a dialog.
/// </remarks>
public sealed class WatchFolderSettings
{
    public bool Enabled { get; set; }

    public string Path { get; set; } = string.Empty;
}
