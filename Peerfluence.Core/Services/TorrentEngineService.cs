using Microsoft.Extensions.Logging;
using PeerSharp.Clients;
using PeerSharp.Config;
using PeerSharp.Interfaces;

namespace Peerfluence.Core.Services;

public sealed class TorrentEngineService : ITorrentEngineService
{
    private readonly IAppSettingsService _settingsService;
    private readonly ILoggerFactory _loggerFactory;
    private IClientEngine? _engine;

    public TorrentEngineService(IAppSettingsService settingsService, ILoggerFactory loggerFactory)
    {
        _settingsService = settingsService;
        _loggerFactory = loggerFactory;
    }

    public IClientEngine Engine => _engine ?? throw new InvalidOperationException("Torrent engine is not initialized.");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _engine ??= CreateEngine();
        await _engine.InitializeAsync(cancellationToken);
        await LoadBlocklistAsync(cancellationToken);
        await LoadGeoIpAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return _engine?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private IClientEngine CreateEngine()
    {
        var settings = _settingsService.Current;

        var clientSettings = new Settings
        {
            Dht = new DhtSettings
            {
                Enabled = settings.Network.EnableDht
            },
            Files = new FilesSettings
            {
                DefaultDownloadPath = settings.Storage.DownloadPath,
                MaxDiskReadSpeed = (uint)Math.Max(0, settings.Network.MaxDiskReadSpeedBytesPerSecond),
                MaxDiskWriteSpeed = (uint)Math.Max(0, settings.Network.MaxDiskWriteSpeedBytesPerSecond)
            },
            Connection = new ConnectionSettings
            {
                TcpPort = GetListeningPort(settings.Network),
                UdpPort = GetListeningPort(settings.Network),
                NatPmpPortMapping = settings.Network.EnableNatPmp,
                UpnpPortMapping = settings.Network.EnableUpnp,
                Encryption = ParseEncryption(settings.EncryptionMode)
            },
            Session = new SessionSettings
            {
                Enabled = settings.Storage.EnableSessionPersistence,
                SessionPath = settings.Storage.SessionPath
            },
            Queue = new PeerSharp.Config.QueueSettings
            {
                Enabled = settings.Queue.EnableQueueManagement,
                MaxActiveDownloads = settings.Queue.MaxActiveDownloads,
                MaxActiveSeeds = settings.Queue.MaxActiveSeeds,
                EnforceAutoStop = true
            },
            Proxy = CreateProxySettings(settings.Proxy)
        };

        var options = new TorrentClientOptions
        {
            LoggerFactory = _loggerFactory,
            Settings = clientSettings
        };

        return ClientEngineFactory.Create(options);
    }

    private static ushort GetListeningPort(Peerfluence.Core.Config.NetworkSettings settings)
    {
        return settings.UseAutomaticListeningPort
            ? (ushort)0
            : (ushort)Math.Clamp(settings.ListeningPort, 1, 65535);
    }

    private static PeerSharp.Config.ProxySettings CreateProxySettings(Peerfluence.Core.Config.ProxySettings proxy)
    {
        var proxySettings = new PeerSharp.Config.ProxySettings
        {
            Type = ParseProxyType(proxy.ProxyType),
            Host = proxy.ProxyHost,
            Port = (ushort)Math.Clamp(proxy.ProxyPort, 0, 65535),
            Username = proxy.ProxyUsername,
            Password = proxy.ProxyPassword,
            ProxyPeers = proxy.ProxyPeers,
            ProxyTrackers = proxy.ProxyTrackers
        };
        return proxySettings;
    }

    private static Encryption ParseEncryption(string mode) => mode switch
    {
        "Refuse" => Encryption.Refuse,
        "Require" => Encryption.Require,
        _ => Encryption.Allow
    };

    private static ProxyType ParseProxyType(string type) => type switch
    {
        "Socks5" => ProxyType.Socks5,
        "Http" => ProxyType.Http,
        _ => ProxyType.None
    };

    private async Task LoadBlocklistAsync(CancellationToken cancellationToken)
    {
        var settings = _settingsService.Current;
        if (!settings.EnableBlocklist || string.IsNullOrWhiteSpace(settings.BlocklistPath))
        {
            return;
        }

        if (!File.Exists(settings.BlocklistPath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(settings.BlocklistPath);
            await Engine.LoadBlocklistAsync(stream, cancellationToken);
        }
        catch
        {
            // Blocklist loading is best-effort
        }
    }

    private async Task LoadGeoIpAsync(CancellationToken cancellationToken)
    {
        var settings = _settingsService.Current;
        if (!settings.EnableGeoIp || string.IsNullOrWhiteSpace(settings.GeoIpPath))
        {
            return;
        }

        if (!File.Exists(settings.GeoIpPath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(settings.GeoIpPath);
            await Engine.LoadGeoIpAsync(stream, cancellationToken);
        }
        catch
        {
            // GeoIP loading is best-effort
        }
    }
}

