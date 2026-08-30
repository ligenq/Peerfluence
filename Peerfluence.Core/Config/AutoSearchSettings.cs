namespace Peerfluence.Core.Config;

/// <summary>
/// A saved query that runs on its own and adds what it finds.
/// </summary>
/// <remarks>
/// <para>
/// One query rather than a list of them, for the same reason the schedule has one window: the
/// interface for managing a list is most of the work and the second entry is rarely wanted. The
/// shape extends to a list later without anything here having to be undone.
/// </para>
/// <para>
/// Anyone running Sonarr or Radarr already has this, and better, through the Transmission RPC
/// endpoint this application serves. This is for the people who do not.
/// </para>
/// </remarks>
public sealed class AutoSearchSettings
{
    public bool Enabled { get; set; }

    public string Query { get; set; } = string.Empty;

    /// <summary>How often to run it. Fifteen minutes is the floor, enforced when it is read.</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>The category new torrents are filed under, or empty for none.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// The links this query has already acted on, so a result is added once and not once an hour.
    /// </summary>
    public List<string> AlreadyAdded { get; set; } = [];
}
