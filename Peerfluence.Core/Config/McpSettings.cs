namespace Peerfluence.Core.Config;

public sealed class McpSettings
{
    public bool Enabled { get; set; }

    public bool AllowDestructiveTools { get; set; }

    public int MaxTorrentPayloadBytes { get; set; } = 10 * 1024 * 1024;
}
