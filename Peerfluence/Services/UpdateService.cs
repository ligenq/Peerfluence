using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack;

namespace Peerfluence.Services;

public sealed class UpdateService : IUpdateService
{
    private readonly ILogger<UpdateService> _logger;
    private readonly IAppSettingsService _settingsService;
    private readonly Func<string, IUpdateManagerAdapter> _updateManagerFactory;
    private IUpdateManagerAdapter? _updateManager;
    private string? _updateManagerUrl;
    private UpdateInfo? _downloadedUpdate;

    public UpdateService(ILogger<UpdateService> logger, IAppSettingsService settingsService)
        : this(logger, settingsService, static updateUrl => new VelopackUpdateManagerAdapter(updateUrl))
    {
    }

    internal UpdateService(ILogger<UpdateService> logger, IAppSettingsService settingsService, Func<string, IUpdateManagerAdapter> updateManagerFactory)
    {
        _logger = logger;
        _settingsService = settingsService;
        _updateManagerFactory = updateManagerFactory;
    }

    public bool IsUpdateAvailable { get; private set; }
    public string? AvailableVersion { get; private set; }

    public bool IsInstalled
    {
        get
        {
            try
            {
                return GetUpdateManager().IsInstalled;
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<bool> CheckForUpdatesAsync()
    {
        if (!IsInstalled)
        {
            _logger.LogDebug("Skipping update check — app is not installed via Velopack");
            return false;
        }

        try
        {
            var mgr = GetUpdateManager();
            var update = await mgr.CheckForUpdatesAsync();
            if (update != null)
            {
                IsUpdateAvailable = true;
                AvailableVersion = update.TargetFullRelease.Version.ToString();
                _logger.LogInformation("Update available: {Version}", AvailableVersion);
                return true;
            }

            _logger.LogDebug("No updates available");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for updates");
            return false;
        }
    }

    public async Task<bool> DownloadUpdateAsync(Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
        {
            return false;
        }

        try
        {
            var mgr = GetUpdateManager();
            var update = await mgr.CheckForUpdatesAsync();
            if (update == null)
            {
                return false;
            }

            await mgr.DownloadUpdatesAsync(update, progress, cancellationToken);
            _downloadedUpdate = update;
            _logger.LogInformation("Update downloaded: {Version}", update.TargetFullRelease.Version);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download update");
            return false;
        }
    }

    public void ApplyUpdateAndRestart(string[]? restartArgs = null)
    {
        if (!IsInstalled)
        {
            return;
        }

        try
        {
            var mgr = GetUpdateManager();
            mgr.ApplyUpdatesAndRestart(_downloadedUpdate?.TargetFullRelease, restartArgs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply update and restart");
        }
    }

    private IUpdateManagerAdapter GetUpdateManager()
    {
        var updateUrl = _settingsService.Current.Update.UpdateUrl;
        if (_updateManager != null && string.Equals(_updateManagerUrl, updateUrl, StringComparison.Ordinal))
        {
            return _updateManager;
        }

        _updateManager = _updateManagerFactory(updateUrl);
        _updateManagerUrl = updateUrl;
        return _updateManager;
    }

    internal interface IUpdateManagerAdapter
    {
        bool IsInstalled { get; }

        Task<UpdateInfo?> CheckForUpdatesAsync();

        Task DownloadUpdatesAsync(UpdateInfo update, Action<int>? progress, CancellationToken cancellationToken);

        void ApplyUpdatesAndRestart(VelopackAsset? asset, string[]? restartArgs);
    }

    internal sealed class VelopackUpdateManagerAdapter : IUpdateManagerAdapter
    {
        private readonly UpdateManager _updateManager;

        public VelopackUpdateManagerAdapter(string updateUrl)
        {
            _updateManager = new UpdateManager(updateUrl);
        }

        public bool IsInstalled => _updateManager.IsInstalled;

        public Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            return _updateManager.CheckForUpdatesAsync();
        }

        public Task DownloadUpdatesAsync(UpdateInfo update, Action<int>? progress, CancellationToken cancellationToken)
        {
            return _updateManager.DownloadUpdatesAsync(update, progress, cancellationToken);
        }

        public void ApplyUpdatesAndRestart(VelopackAsset? asset, string[]? restartArgs)
        {
            _updateManager.ApplyUpdatesAndRestart(asset, restartArgs);
        }
    }
}

