using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PeerSharp.Core;

namespace Peerfluence.Services;

/// <summary>
/// Reports data actually exchanged with peers of a chosen BitTorrent client, so interoperability can
/// be confirmed against real software rather than inferred.
///
/// <para>
/// Defaults to Transmission because that is the implementation whose behaviour is currently open:
/// PeerSharp's opening sequence, message framing and MSE handshake have all been verified conformant,
/// but a live seeding run had incomplete Transmission peers connect without ever requesting a piece.
/// Bytes moving in either direction settle that; nothing else does.
/// </para>
///
/// <para>
/// Read-only. It polls the peer list that any consumer of the library can see and never influences
/// what the engine does, so a run with logging on behaves exactly like one without.
/// </para>
/// </summary>
public sealed class PeerInteropLoggingHostedService : IHostedService
{
    /// <summary>
    /// Client name prefix to watch. Matches <see cref="PeerInfo.ClientName"/>, which carries the name
    /// and version decoded from the peer id, so a prefix covers every version.
    /// </summary>
    private const string DefaultClientFilter = "Transmission";

    private const string ClientFilterVariable = "PEERFLUENCE_INTEROP_CLIENT";
    private const string EnabledVariable = "PEERFLUENCE_INTEROP_LOG";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SummaryInterval = TimeSpan.FromMinutes(1);

    private readonly ITorrentService _torrentService;
    private readonly ILogger<PeerInteropLoggingHostedService> _logger;
    private readonly Dictionary<PeerKey, PeerTotals> _seen = [];

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private string _clientFilter = DefaultClientFilter;
    private DateTimeOffset _nextSummary;

    public PeerInteropLoggingHostedService(ITorrentService torrentService, ILogger<PeerInteropLoggingHostedService> logger)
    {
        _torrentService = torrentService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Off unless asked for. This is a diagnostic aid, and a line per peer per two seconds is not
        // something an ordinary run should be paying for.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnabledVariable)))
        {
            return Task.CompletedTask;
        }

        var configured = Environment.GetEnvironmentVariable(ClientFilterVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _clientFilter = configured.Trim();
        }

        _logger.LogInformation(
            "Peer interop logging is on, watching clients whose name starts with '{ClientFilter}'. " +
            "Set {ClientVariable} to watch a different one, or unset {EnabledVariable} to turn this off.",
            _clientFilter,
            ClientFilterVariable,
            EnabledVariable);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _nextSummary = DateTimeOffset.UtcNow + SummaryInterval;
        _monitorTask = MonitorAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);

        if (_monitorTask is not null)
        {
            try
            {
                await _monitorTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* Shutting down. */ }
            catch (TimeoutException) { /* Do not hold up shutdown for a diagnostic. */ }
        }

        LogSummary();

        _cts.Dispose();
        _cts = null;
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Poll();

                if (DateTimeOffset.UtcNow >= _nextSummary)
                {
                    LogSummary();
                    _nextSummary = DateTimeOffset.UtcNow + SummaryInterval;
                }
            }
            catch (Exception ex)
            {
                // A diagnostic must never take the app down with it.
                _logger.LogDebug(ex, "Peer interop logging poll failed");
            }

            try
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Poll()
    {
        foreach (var torrent in _torrentService.GetTorrents())
        {
            foreach (var peer in torrent.Peers.GetConnectedPeers())
            {
                if (!peer.ClientName.StartsWith(_clientFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var key = new PeerKey(torrent.Name, peer.EndPoint);
                if (!_seen.TryGetValue(key, out var totals))
                {
                    totals = new PeerTotals(peer.ClientName);
                    _seen[key] = totals;

                    _logger.LogInformation(
                        "[interop] connected to {Client} at {EndPoint} on '{Torrent}' ({Transport}, {Encryption}, peer progress {Progress:P0})",
                        peer.ClientName,
                        peer.EndPoint,
                        peer.IsUtp ? "uTP" : "TCP",
                        peer.IsEncrypted ? "encrypted" : "plaintext",
                        peer.Progress);
                }

                // Per-peer byte counters are cumulative, so a rise is new data on the wire.
                long uploadedDelta = Math.Max(0, peer.Uploaded - totals.Uploaded);
                long downloadedDelta = Math.Max(0, peer.Downloaded - totals.Downloaded);

                if (uploadedDelta > 0)
                {
                    // The direction that was in doubt: we are the one serving.
                    _logger.LogInformation(
                        "[interop] SENT {Delta} to {Client} at {EndPoint} on '{Torrent}' ({Transport}, {Encryption}) - {Total} total",
                        Describe(uploadedDelta),
                        peer.ClientName,
                        peer.EndPoint,
                        torrent.Name,
                        peer.IsUtp ? "uTP" : "TCP",
                        peer.IsEncrypted ? "encrypted" : "plaintext",
                        Describe(peer.Uploaded));
                }

                if (downloadedDelta > 0)
                {
                    _logger.LogInformation(
                        "[interop] RECEIVED {Delta} from {Client} at {EndPoint} on '{Torrent}' ({Transport}, {Encryption}) - {Total} total",
                        Describe(downloadedDelta),
                        peer.ClientName,
                        peer.EndPoint,
                        torrent.Name,
                        peer.IsUtp ? "uTP" : "TCP",
                        peer.IsEncrypted ? "encrypted" : "plaintext",
                        Describe(peer.Downloaded));
                }

                totals.Update(peer);
            }
        }
    }

    /// <summary>
    /// The periodic tally. Individual transfers answer "did anything move"; this answers "how many of
    /// the peers we met actually exchanged data", which is the part a single line cannot show.
    /// </summary>
    private void LogSummary()
    {
        if (_seen.Count == 0)
        {
            _logger.LogInformation(
                "[interop] no {ClientFilter} peers seen yet. Swarm composition decides this - try a torrent with " +
                "active peers of that client, or run one yourself on the same swarm.",
                _clientFilter);
            return;
        }

        int served = _seen.Values.Count(static t => t.Uploaded > 0);
        int received = _seen.Values.Count(static t => t.Downloaded > 0);
        int wantedOurs = _seen.Values.Count(static t => t.EverInterestedInUs);
        int incomplete = _seen.Values.Count(static t => !t.IsSeed);
        long totalUp = _seen.Values.Sum(static t => t.Uploaded);
        long totalDown = _seen.Values.Sum(static t => t.Downloaded);

        _logger.LogInformation(
            "[interop] {ClientFilter} summary: {Count} peer(s) met, {Incomplete} incomplete, {WantedOurs} asked us for data, " +
            "{Served} received data from us ({TotalUp}), {Received} sent us data ({TotalDown})",
            _clientFilter,
            _seen.Count,
            incomplete,
            wantedOurs,
            served,
            Describe(totalUp),
            received,
            Describe(totalDown));

        if (incomplete > 0 && served == 0)
        {
            _logger.LogWarning(
                "[interop] {Incomplete} incomplete {ClientFilter} peer(s) connected and none received a byte from us. " +
                "That is the case worth reporting - it means we advertised data and never delivered it.",
                incomplete,
                _clientFilter);
        }
    }

    private static string Describe(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0 / 1024.0:F2} MiB"),
            >= 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:F1} KiB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} B")
        };
    }

    private readonly record struct PeerKey(string Torrent, IPEndPoint EndPoint);

    private sealed class PeerTotals(string clientName)
    {
        public string ClientName { get; } = clientName;
        public long Uploaded { get; private set; }
        public long Downloaded { get; private set; }
        public bool EverInterestedInUs { get; private set; }

        /// <summary>
        /// Whether the peer ever had everything. A seed will not ask us for data however well we
        /// behave, so counting one as a snub would manufacture a problem that is not there.
        /// </summary>
        public bool IsSeed { get; private set; }

        public void Update(PeerInfo peer)
        {
            Uploaded = Math.Max(Uploaded, peer.Uploaded);
            Downloaded = Math.Max(Downloaded, peer.Downloaded);
            EverInterestedInUs |= peer.PeerInterested;
            IsSeed |= peer.Progress >= 1.0f;
        }
    }
}
