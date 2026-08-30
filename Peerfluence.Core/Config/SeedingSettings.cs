namespace Peerfluence.Core.Config;

/// <summary>
/// When to stop seeding a torrent that nobody has told individually.
/// </summary>
/// <remarks>
/// PeerSharp enforces a ratio and a seeding time per torrent, and the add dialog and the details
/// pane have always been able to set them. What was missing was a default: without one, every
/// torrent seeds for ever unless somebody remembers to say otherwise, one torrent at a time.
/// </remarks>
public sealed class SeedingSettings
{
    /// <summary>Whether new torrents stop seeding once they have given back enough.</summary>
    public bool LimitRatio { get; set; }

    /// <summary>Uploaded over downloaded. Two means the torrent has given back twice what it took.</summary>
    public float RatioLimit { get; set; } = 2.0f;

    /// <summary>Whether new torrents stop seeding after a while regardless of ratio.</summary>
    public bool LimitSeedTime { get; set; }

    /// <summary>How long, in minutes. A day by default.</summary>
    public int SeedTimeLimitMinutes { get; set; } = 1440;
}
