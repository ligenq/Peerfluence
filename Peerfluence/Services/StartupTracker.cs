using System.Diagnostics;

namespace Peerfluence.Services;

/// <summary>
/// Provides a single monotonic clock for startup diagnostics across the generic
/// host and Avalonia application lifecycle.
/// </summary>
public sealed class StartupTracker
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;
}
