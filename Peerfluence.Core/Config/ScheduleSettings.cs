namespace Peerfluence.Core.Config;

/// <summary>
/// A time of day during which different speed limits apply.
/// </summary>
/// <remarks>
/// One window rather than a table of them. The thing people actually want is "leave the connection
/// alone while I am using it", which is one window on the days they are there, and a table of
/// windows is a great deal of interface for the second one.
/// </remarks>
public sealed class ScheduleSettings
{
    public bool Enabled { get; set; }

    /// <summary>When the window opens, as HH:mm.</summary>
    public string From { get; set; } = "08:00";

    /// <summary>When it closes, as HH:mm. Earlier than <see cref="From"/> means it crosses midnight.</summary>
    public string To { get; set; } = "18:00";

    /// <summary>The limit inside the window. Zero is unlimited, as everywhere else.</summary>
    public long DownloadLimitBytesPerSecond { get; set; }

    /// <inheritdoc cref="DownloadLimitBytesPerSecond"/>
    public long UploadLimitBytesPerSecond { get; set; }

    public bool Monday { get; set; } = true;

    public bool Tuesday { get; set; } = true;

    public bool Wednesday { get; set; } = true;

    public bool Thursday { get; set; } = true;

    public bool Friday { get; set; } = true;

    public bool Saturday { get; set; } = true;

    public bool Sunday { get; set; } = true;
}
