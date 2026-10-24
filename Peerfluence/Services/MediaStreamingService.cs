using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PeerSharp.Interfaces;
using PeerSharp.Streaming;

namespace Peerfluence.Services;

/// <summary>Owns the HTTP listener and the lifetime of one local or shared playback session.</summary>
public sealed class MediaStreamingService : IMediaStreamingService
{
    private readonly IAppSettingsService _settings;
    private readonly ITorrentEngineService _engine;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Lock _gate = new();
    private Session? _active;
    private bool _disposed;

    public MediaStreamingService(IAppSettingsService settings, ITorrentEngineService engine, ILoggerFactory loggerFactory)
    {
        _settings = settings;
        _engine = engine;
        _loggerFactory = loggerFactory;
    }

    public async Task<MediaStreamInfo> StartAsync(ITorrent torrent, int fileIndex, bool share, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        cancellationToken.ThrowIfCancellationRequested();
        if (!torrent.StreamableFileIndices.Contains(fileIndex))
        {
            throw new ArgumentOutOfRangeException(nameof(fileIndex));
        }

        var settings = _settings.Current.Streaming;
        var address = IPAddress.Loopback;
        if (share)
        {
            // A blank address is refused rather than quietly served on loopback. The caller asked
            // for a link to hand to another device, and a 127.0.0.1 link is not one - nothing about
            // the copied URL would have said so, and PeerSharp tokenises only a non-loopback bind.
            if (!StreamingSettings.IsShareableAddress(settings.BindAddress))
            {
                throw new ArgumentException(Properties.Resources.Settings_StreamingAddressInvalid);
            }

            address = IPAddress.Parse(settings.BindAddress.Trim());
        }

        _engine.ApplyStreamingSettings();
        // PeerSharp 5.1 prioritizes each open stream without changing DownloadStrategy.
        var server = new HttpStreamServer(torrent, fileIndex, new HttpStreamServerOptions
        {
            BindAddress = address,
            Port = share ? settings.Port : 0,
        }, _loggerFactory);
        var session = new Session(server, cancellationToken);
        Session? previous;
        lock (_gate)
        {
            if (_disposed)
            {
                session.Dispose();
                throw new ObjectDisposedException(nameof(MediaStreamingService));
            }

            previous = _active;
            _active = session;
        }

        previous?.Dispose();
        try
        {
            server.Start();
            if (!torrent.Started)
            {
                await torrent.StartAsync(session.Token).ConfigureAwait(false);
            }
            // Metadata and data waits obey Settings.Streaming and this session's cancellation.
            // Preparing one verified block avoids handing the player an unready stream.
            await using (var stream = await torrent.OpenStreamAsync(fileIndex, session.Token).ConfigureAwait(false))
            {
                var buffer = new byte[256 * 1024];
                if (await stream.ReadAsync(buffer, session.Token).ConfigureAwait(false) == 0)
                {
                    throw new EndOfStreamException(Properties.Resources.Details_StreamNoData);
                }
            }

            session.Token.ThrowIfCancellationRequested();
            return new MediaStreamInfo(server.Url, server.ContentType);
        }
        catch
        {
            lock (_gate)
            {
                if (ReferenceEquals(_active, session)) _active = null;
            }

            session.Dispose();
            throw;
        }
    }

    public void Play(MediaStreamInfo stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var player = _settings.Current.MediaPlayerPath;
        if (string.IsNullOrWhiteSpace(player))
        {
            Process.Start(new ProcessStartInfo(stream.Url) { UseShellExecute = true });
        }
        else
        {
            var start = new ProcessStartInfo(player) { UseShellExecute = false };
            start.ArgumentList.Add(stream.Url);
            Process.Start(start);
        }
    }

    public void EndSession()
    {
        Session? session;
        lock (_gate)
        {
            session = _active;
            _active = null;
        }

        session?.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        EndSession();
    }

    private sealed class Session : IDisposable
    {
        private readonly HttpStreamServer _server;
        private readonly CancellationTokenSource _cancellation;
        private readonly CancellationTokenRegistration _registration;
        private int _disposed;

        public Session(HttpStreamServer server, CancellationToken cancellationToken)
        {
            _server = server;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Token = _cancellation.Token;
            _registration = Token.Register(server.Dispose);
        }

        public CancellationToken Token { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _cancellation.Cancel();
            _registration.Dispose();
            _server.Dispose();
            _cancellation.Dispose();
        }
    }
}
