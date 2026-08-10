using Peerfluence.Core.Config;
using System.IO.Abstractions;

namespace Peerfluence.Core.Services;

public sealed class AppSettingsService : IAppSettingsService
{
    private readonly IAppPaths _paths;
    private readonly IAppSettingsStore _store;
    private readonly IFileSystem _fileSystem;

    public AppSettingsService(IAppPaths paths, IAppSettingsStore store, IFileSystem fileSystem)
    {
        _paths = paths;
        _store = store;
        _fileSystem = fileSystem;
    }

    public AppSettings Current { get; private set; } = new();

    public AppSettings CreateDefaultSettings()
    {
        var settings = CreateDefaults();
        EnsureDefaultsExist(settings);
        return settings;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var loaded = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        Current = loaded ?? CreateDefaults();

        var changed = EnsureDefaultsExist(Current);
        if (loaded == null || changed)
        {
            await _store.SaveAsync(Current, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        EnsureDefaultsExist(Current);
        return _store.SaveAsync(Current, cancellationToken);
    }

    private AppSettings CreateDefaults()
    {
        return new AppSettings
        {
            Storage =
            {
                DownloadPath = _paths.DefaultDownloadDirectory,
                SessionPath = _paths.SessionDirectory,
                EnableSessionPersistence = true
            },
            Network =
            {
                EnableDht = true,
                AnswerInfoHashSampling = true,
                AllowMultipleConnectionsPerIp = true,
                EnableNatPmp = true,
                EnableUpnp = false,
                UseAutomaticListeningPort = false,
                ListeningPort = 55125,
                MaxDiskReadSpeedBytesPerSecond = 0,
                MaxDiskWriteSpeedBytesPerSecond = 0
            },
            Theme =
            {
                ThemeVariant = "System",
                ColorTheme = "Indigo",
                BackgroundStyle = "GradientSoft"
            },
            ShowRemoveTorrentOptions = true,
            ShowDetailsPane = false,
            DefaultRemoveTorrentAction = "RemoveOnly",
            Language = "en-US"
        };
    }

    private bool EnsureDefaultsExist(AppSettings settings)
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(settings.Storage.DownloadPath))
        {
            settings.Storage.DownloadPath = _paths.DefaultDownloadDirectory;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.Storage.SessionPath))
        {
            settings.Storage.SessionPath = _paths.SessionDirectory;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(settings.Storage.DownloadPath))
        {
            _fileSystem.Directory.CreateDirectory(settings.Storage.DownloadPath);
        }

        if (!string.IsNullOrWhiteSpace(settings.Storage.SessionPath))
        {
            _fileSystem.Directory.CreateDirectory(settings.Storage.SessionPath);
        }

        if (string.IsNullOrWhiteSpace(settings.Theme.ThemeVariant))
        {
            settings.Theme.ThemeVariant = "System";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.Theme.ColorTheme))
        {
            settings.Theme.ColorTheme = "Indigo";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.Theme.BackgroundStyle))
        {
            settings.Theme.BackgroundStyle = "GradientSoft";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.Language))
        {
            settings.Language = "en-US";
            changed = true;
        }

        if (!IsKnownRemoveTorrentAction(settings.DefaultRemoveTorrentAction))
        {
            settings.DefaultRemoveTorrentAction = "RemoveOnly";
            changed = true;
        }

        if (settings.CompletionAction == null)
        {
            settings.CompletionAction = new CompletionActionSettings();
            changed = true;
        }

        if (settings.Mcp == null)
        {
            settings.Mcp = new McpSettings();
            changed = true;
        }
        if (settings.CompletionAction.TimeoutSeconds <= 0)
        {
            settings.CompletionAction.TimeoutSeconds = 300;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.CompletionAction.WorkingDirectoryTemplate))
        {
            settings.CompletionAction.WorkingDirectoryTemplate = "{downloadPath}";
            changed = true;
        }

        if (settings.Network.MaxDiskReadSpeedBytesPerSecond < 0)
        {
            settings.Network.MaxDiskReadSpeedBytesPerSecond = 0;
            changed = true;
        }

        if (settings.Network.MaxDiskWriteSpeedBytesPerSecond < 0)
        {
            settings.Network.MaxDiskWriteSpeedBytesPerSecond = 0;
            changed = true;
        }

        var listeningPort = settings.Network.ListeningPort <= 0
            ? 55125
            : Math.Clamp(settings.Network.ListeningPort, 1, 65535);
        if (settings.Network.ListeningPort != listeningPort)
        {
            settings.Network.ListeningPort = listeningPort;
            changed = true;
        }

        if (settings.Mcp.MaxTorrentPayloadBytes <= 0)
        {
            settings.Mcp.MaxTorrentPayloadBytes = 10 * 1024 * 1024;
            changed = true;
        }

        return changed;
    }

    private static bool IsKnownRemoveTorrentAction(string? action)
    {
        return action is "RemoveOnly" or "DeleteFiles" or "DeleteMetadata" or "DeleteAll";
    }
}
