namespace Peerfluence.Core.Config;

public sealed class NetworkSettings
{
    public bool EnableDht { get; set; } = true;

    public bool EnableNatPmp { get; set; } = true;

    public bool EnableUpnp { get; set; } = false;

    public bool UseAutomaticListeningPort { get; set; } = false;

    public int ListeningPort { get; set; } = 55125;

    public long MaxDiskReadSpeedBytesPerSecond { get; set; } = 0;

    public long MaxDiskWriteSpeedBytesPerSecond { get; set; } = 0;
}

