namespace Peerfluence.Core.Config;

public sealed class QueueSettings
{
    public bool EnableQueueManagement { get; set; }

    public int MaxActiveDownloads { get; set; } = 3;

    public int MaxActiveSeeds { get; set; } = 2;
}

