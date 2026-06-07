using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace Peerfluence.Services;

public sealed class UpdateService : IUpdateService
{
    private readonly ILogger<UpdateService> _logger;
    private readonly IAppSettingsService _settingsService;
    private readonly Func<string, IUpdateManagerAdapter> _updateManagerFactory;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
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

    public UpdateChannel Channel => UpdateChannel.DirectDownload;

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

    public bool CanCheckForUpdates => IsInstalled;

    public bool CanApplyUpdates => IsInstalled;

    public async Task<bool> CheckForUpdatesAsync()
    {
        if (!IsInstalled)
        {
            _logger.LogDebug("Skipping update check — app is not installed via Velopack");
            return false;
        }

        await _updateLock.WaitAsync();
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

            IsUpdateAvailable = false;
            AvailableVersion = null;
            _downloadedUpdate = null;
            _logger.LogDebug("No updates available");
            return false;
        }
        catch (Exception ex)
        {
            IsUpdateAvailable = false;
            AvailableVersion = null;
            _logger.LogWarning(ex, "Failed to check for updates");
            return false;
        }
        finally
        {
            _updateLock.Release();
        }
    }

    public async Task<bool> DownloadUpdateAsync(Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
        {
            return false;
        }

        await _updateLock.WaitAsync(cancellationToken);
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
        finally
        {
            _updateLock.Release();
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
        var updateUrl = NormalizeUpdateUrl(_settingsService.Current.Update.UpdateUrl);
        if (_updateManager != null && string.Equals(_updateManagerUrl, updateUrl, StringComparison.Ordinal))
        {
            return _updateManager;
        }

        _updateManager = _updateManagerFactory(updateUrl);
        _updateManagerUrl = updateUrl;
        return _updateManager;
    }

    internal static string NormalizeUpdateUrl(string updateUrl)
    {
        var trimmed = updateUrl.Trim();
        if (!TryGetGithubRepositoryUrl(trimmed, out var repositoryUrl))
        {
            return trimmed;
        }

        return repositoryUrl;
    }

    private static bool TryGetGithubRepositoryUrl(string updateUrl, out string repositoryUrl)
    {
        repositoryUrl = string.Empty;
        if (!Uri.TryCreate(updateUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        repositoryUrl = $"https://github.com/{segments[0]}/{segments[1]}";
        return true;
    }

    internal static bool IsGithubRepositoryUrl(string updateUrl)
    {
        return TryGetGithubRepositoryUrl(updateUrl, out _);
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
            var locator = CreateLocator();
            _updateManager = IsGithubRepositoryUrl(updateUrl)
                ? new UpdateManager(new GithubSource(updateUrl, accessToken: null, prerelease: false, downloader: null), options: null, locator)
                : new UpdateManager(updateUrl, options: null, locator);
        }

        private static IVelopackLocator? CreateLocator()
        {
            return OperatingSystem.IsWindows()
                ? CreateWindowsLocator()
                : null;
        }

        [SupportedOSPlatform("windows")]
        private static IVelopackLocator CreateWindowsLocator()
        {
            var logger = new NullVelopackLogger();
            return new WindowsVelopackLocator(new DefaultProcessImpl(logger), logger);
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

