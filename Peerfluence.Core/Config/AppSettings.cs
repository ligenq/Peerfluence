namespace Peerfluence.Core.Config;

public sealed class AppSettings
{
    public StorageSettings Storage { get; set; } = new();

    public NetworkSettings Network { get; set; } = new();

    public ThemeSettings Theme { get; set; } = new();

    public QueueSettings Queue { get; set; } = new();

    public SeedingSettings Seeding { get; set; } = new();

    public ProxySettings Proxy { get; set; } = new();

    public UpdateSettings Update { get; set; } = new();

    public CompletionActionSettings CompletionAction { get; set; } = new();

    public McpSettings Mcp { get; set; } = new();

    public CategorySettings Categories { get; set; } = new();

    public RemoteSettings Remote { get; set; } = new();

    public SearchSettings Search { get; set; } = new();

    /// <summary>
    /// "Simple" or "Advanced". Empty means the user has not been asked yet, which is what brings up
    /// the welcome on first launch; anything unrecognised is read as Advanced, so a hand-edited or
    /// downgraded settings file never hides features someone was already using.
    /// </summary>
    public string InterfaceMode { get; set; } = string.Empty;

    public bool ShowAddTorrentOptions { get; set; } = true;

    public bool ShowRemoveTorrentOptions { get; set; } = true;

    /// <summary>
    /// Whether the details pane sits under the torrent list. Off by default: it is a second view of
    /// one torrent, and while it is closed the list has the whole window to fill.
    /// </summary>
    public bool ShowDetailsPane { get; set; }

    public bool AssociateTorrentFiles { get; set; }

    public bool AssociateMagnetLinks { get; set; }

    public string DefaultRemoveTorrentAction { get; set; } = "RemoveOnly";

    public string Language { get; set; } = "en-US";

    public string MediaPlayerPath { get; set; } = string.Empty;

    public string EncryptionMode { get; set; } = "Allow";

    public bool EnableBlocklist { get; set; } = false;

    public string BlocklistPath { get; set; } = string.Empty;

    public bool EnableGeoIp { get; set; } = false;

    public string GeoIpPath { get; set; } = string.Empty;
}

