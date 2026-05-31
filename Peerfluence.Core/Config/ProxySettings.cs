namespace Peerfluence.Core.Config;

public sealed class ProxySettings
{
    public string ProxyType { get; set; } = "None";

    public string ProxyHost { get; set; } = string.Empty;

    public int ProxyPort { get; set; } = 0;

    public string ProxyUsername { get; set; } = string.Empty;

    public string ProxyPassword { get; set; } = string.Empty;

    public bool ProxyPeers { get; set; } = true;

    public bool ProxyTrackers { get; set; } = true;
}

