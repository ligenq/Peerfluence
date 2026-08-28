using System.Diagnostics.Metrics;
using PeerSharp.Diagnostics;

namespace Peerfluence.Core.Services;

/// <summary>
/// Reads the engine's own metrics, which say things <see cref="PeerSharp.Core.EngineStats"/> cannot.
/// </summary>
/// <remarks>
/// <para>
/// PeerSharp 4.0 publishes to a <see cref="Meter"/> named <c>PeerSharp</c>. The useful difference
/// from the stats snapshot is the byte totals: those cover the engine's whole life, including
/// torrents since removed, so they only ever increase, where <c>EngineStats</c> reports what the
/// torrents present right now have transferred. "How much has this machine actually pulled down
/// today" is the first question and the snapshot has never been able to answer it.
/// </para>
/// <para>
/// Every instrument is observable, so nothing is measured until something asks. Asking costs one
/// pass over the torrent list, which is why this reads on demand rather than on a timer.
/// </para>
/// </remarks>
public sealed class EngineMetricsReader : IEngineMetricsReader, IDisposable
{
    private readonly MeterListener _listener;
    private readonly Lock _gate = new();

    /// <summary>
    /// Serialises whole reads. <see cref="Read"/> clears and then polls, so two at once would each
    /// see part of the other's measurements.
    /// </summary>
    private readonly Lock _readGate = new();
    private readonly Dictionary<string, long> _values = [];
    private bool _disposed;

    public EngineMetricsReader()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (instrument.Meter.Name == PeerSharpMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };

        _listener.SetMeasurementEventCallback<long>(OnMeasurement);
        _listener.SetMeasurementEventCallback<double>(
            (instrument, measurement, _, state) => OnMeasurement(instrument, (long)measurement, default, state));
        _listener.Start();
    }

    private void OnMeasurement(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        lock (_gate)
        {
            // Summed rather than assigned: with several engines in one process each publishes its
            // own instrument, and the totals across them are what a single-engine application wants
            // either way. Peerfluence runs one, so this is the same number by a route that does not
            // silently take only the last engine's if that ever changes.
            _values[instrument.Name] = _values.GetValueOrDefault(instrument.Name) + measurement;
        }
    }

    public EngineMetricsSnapshot Read()
    {
        if (_disposed)
        {
            return default;
        }

        lock (_readGate)
        {
            lock (_gate)
            {
                _values.Clear();
            }

            // Observable instruments produce nothing until polled, so this is what makes the
            // callbacks above fire at all.
            _listener.RecordObservableInstruments();

            lock (_gate)
            {
                return new EngineMetricsSnapshot(
                    _values.GetValueOrDefault(PeerSharpMetrics.DownloadedInstrument),
                    _values.GetValueOrDefault(PeerSharpMetrics.UploadedInstrument),
                    _values.GetValueOrDefault(PeerSharpMetrics.DownloadSpeedInstrument),
                    _values.GetValueOrDefault(PeerSharpMetrics.UploadSpeedInstrument),
                    _values.GetValueOrDefault(PeerSharpMetrics.TorrentsInstrument),
                    _values.GetValueOrDefault(PeerSharpMetrics.ActiveTorrentsInstrument),
                    _values.GetValueOrDefault(PeerSharpMetrics.ConnectedPeersInstrument));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listener.Dispose();
    }
}

/// <summary>Reads the engine's published metrics on demand.</summary>
public interface IEngineMetricsReader
{
    /// <summary>
    /// Polls the engine's observable instruments and returns what they reported. All zeros before
    /// the engine exists, which is not distinguishable from an engine that has done nothing.
    /// </summary>
    EngineMetricsSnapshot Read();
}

/// <summary>
/// One poll of the engine's meter.
/// </summary>
/// <param name="LifetimeDownloadedBytes">
/// Bytes downloaded over the engine's whole life, including by torrents since removed.
/// </param>
/// <param name="LifetimeUploadedBytes">Bytes uploaded over the engine's whole life.</param>
/// <param name="DownloadSpeedBytesPerSecond">Aggregate download rate.</param>
/// <param name="UploadSpeedBytesPerSecond">Aggregate upload rate.</param>
/// <param name="Torrents">Torrents the engine is managing.</param>
/// <param name="ActiveTorrents">Torrents downloading, checking or fetching metadata.</param>
/// <param name="ConnectedPeers">Connected peers across all torrents.</param>
public readonly record struct EngineMetricsSnapshot(
    long LifetimeDownloadedBytes,
    long LifetimeUploadedBytes,
    long DownloadSpeedBytesPerSecond,
    long UploadSpeedBytesPerSecond,
    long Torrents,
    long ActiveTorrents,
    long ConnectedPeers);
