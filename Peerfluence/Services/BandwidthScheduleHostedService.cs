using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Peerfluence.Core.Services;

namespace Peerfluence.Services;

/// <summary>
/// Puts the scheduled speed limits into force when the clock reaches them.
/// </summary>
/// <remarks>
/// <para>
/// It does nothing but ask the engine service to apply its limits, once a minute. Whether the window
/// is open is decided in <see cref="BandwidthSchedule"/>, and what to do about it in
/// <c>ApplySpeedLimits</c>, which the settings screen already calls when a limit changes. So the
/// clock is not a second way of setting limits; it is a second reason to ask the one way.
/// </para>
/// <para>
/// A minute is the resolution the window is expressed in, and applying limits is two property
/// assignments, so there is nothing to be gained by being cleverer about when to wake up.
/// </para>
/// </remarks>
internal sealed class BandwidthScheduleHostedService : BackgroundService
{
    private readonly ITorrentEngineService _engineService;
    private readonly TimeProvider _timeProvider;

    public BandwidthScheduleHostedService(ITorrentEngineService engineService, TimeProvider timeProvider)
    {
        _engineService = engineService;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The engine may restore and begin transferring before the first minute elapses. Apply the
        // limits once immediately, then keep them aligned with the clock.
        _engineService.ApplySpeedLimits();
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                _engineService.ApplySpeedLimits();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
