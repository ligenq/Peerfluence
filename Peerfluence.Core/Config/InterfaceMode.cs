namespace Peerfluence.Core.Config;

/// <summary>
/// How much of the application the user wants to see.
/// </summary>
public enum InterfaceMode
{
    /// <summary>
    /// Downloading and little else: one add action, a list, and pause and open-folder per row.
    /// </summary>
    Simple,

    /// <summary>
    /// Everything - the dashboard, the details pane, per-file selection, queueing and the rest.
    /// </summary>
    Advanced
}
