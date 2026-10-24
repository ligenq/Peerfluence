using PeerSharp.Core;

namespace Peerfluence.Core.Messaging;

/// <summary>
/// The engine statistics were just read, on the UI thread.
/// </summary>
/// <remarks>
/// The downloads screen reads them once a second for its own totals and sends the reading on, so a
/// second screen that shows the same numbers can use it rather than reading the engine again.
/// </remarks>
public sealed class EngineStatsReadMessage
{
    public EngineStatsReadMessage(EngineStats stats)
    {
        Stats = stats;
    }

    public EngineStats Stats { get; }
}
