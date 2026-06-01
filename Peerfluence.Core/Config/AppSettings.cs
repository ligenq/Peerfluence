namespace Peerfluence.Core.Config;

public sealed class AppSettings
{
    public StorageSettings Storage { get; set; } = new();

    public NetworkSettings Network { get; set; } = new();

    public ThemeSettings Theme { get; set; } = new();

    public QueueSettings Queue { get; set; } = new();

    public ProxySettings Proxy { get; set; } = new();

    public UpdateSettings Update { get; set; } = new();

    public CompletionActionSettings CompletionAction { get; set; } = new();

    public McpSettings Mcp { get; set; } = new();

    public bool ShowAddTorrentOptions { get; set; } = true;

    public bool ShowRemoveTorrentOptions { get; set; } = true;

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

