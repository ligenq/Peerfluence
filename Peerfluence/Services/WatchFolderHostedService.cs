using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Peerfluence.Core.Services;

namespace Peerfluence.Services;

/// <summary>
/// Adds torrent files that appear in a watched directory.
/// </summary>
/// <remarks>
/// <para>
/// A sweep on startup and a <see cref="FileSystemWatcher"/> afterwards. The sweep matters as much as
/// the watcher: files put there while the application was closed are the common case, and a watcher
/// only ever reports what happens while it is listening.
/// </para>
/// <para>
/// Every decision this makes lives in <see cref="WatchFolder"/>, where it can be tested without a
/// directory. What is left here is the plumbing.
/// </para>
/// </remarks>
internal sealed class WatchFolderHostedService : IHostedService, IDisposable
{
    private const int AddAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly IAppSettingsService _settingsService;
    private readonly ITorrentService _torrentService;
    private readonly ILogger<WatchFolderHostedService> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly SemaphoreSlim _configurationLock = new(1, 1);
    private readonly SemaphoreSlim _addLock = new(1, 1);

    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public WatchFolderHostedService(
        IAppSettingsService settingsService,
        ITorrentService torrentService,
        ILogger<WatchFolderHostedService> logger)
    {
        _settingsService = settingsService;
        _torrentService = torrentService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _settingsService.SettingsSaved += OnSettingsSavedAsync;
        await ReconfigureAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        BeginStopping();

        // Do not let the engine stop while an event raised just before shutdown is still adding a
        // torrent. Hosted services stop in reverse registration order, and this service is before
        // the engine in that sequence precisely so it can drain here.
        await _configurationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DisposeWatcher();
        }
        finally
        {
            _configurationLock.Release();
        }
        await _addLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        _addLock.Release();
    }

    public void Dispose()
    {
        BeginStopping();
        _configurationLock.Wait();
        try
        {
            DisposeWatcher();
        }
        finally
        {
            _configurationLock.Release();
        }
    }

    private void BeginStopping()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settingsService.SettingsSaved -= OnSettingsSavedAsync;
        _stopping.Cancel();
    }

    private void DisposeWatcher()
    {
        if (_watcher is not null)
        {
            _watcher.Created -= OnAppeared;
            _watcher.Changed -= OnAppeared;
            _watcher.Renamed -= OnAppeared;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    /// <summary>Starts, stops, or moves the watcher to match the settings just saved.</summary>
    internal async Task ReconfigureAsync(CancellationToken cancellationToken)
    {
        await _configurationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            var settings = _settingsService.Current.WatchFolder;
            var desiredPath = settings.Enabled
                && !string.IsNullOrWhiteSpace(settings.Path)
                && Directory.Exists(settings.Path)
                    ? Path.GetFullPath(settings.Path)
                    : null;

            if (_watcher is not null
                && string.Equals(_watcher.Path, desiredPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DisposeWatcher();
            if (desiredPath is null)
            {
                return;
            }

            // Listen before sweeping so a file arriving between those two operations is not lost.
            // AddAsync serializes and rechecks existence, so seeing the same file in both places is
            // harmless.
            var watcher = new FileSystemWatcher(desiredPath, "*.torrent")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            };
            watcher.Created += OnAppeared;
            watcher.Changed += OnAppeared;
            watcher.Renamed += OnAppeared;
            _watcher = watcher;
            watcher.EnableRaisingEvents = true;

            await SweepAsync(desiredPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _configurationLock.Release();
        }
    }

    private async Task OnSettingsSavedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReconfigureAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // The application is shutting down.
        }
        catch (Exception ex)
        {
            // The settings themselves were saved successfully. A bad or inaccessible watch path
            // should disable this feature, not make the whole settings save look as though it failed.
            _logger.LogWarning(ex, "Could not apply the watched-folder settings");
        }
    }

    /// <summary>Takes everything already sitting in the directory.</summary>
    internal async Task SweepAsync(string directory, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.torrent"))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await AddAsync(file, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Adds one file and marks it, or leaves it alone and says why.
    /// </summary>
    /// <remarks>
    /// A file that fails is left exactly as it is, unmarked, so the next sweep tries again. That is
    /// the right answer for the usual failure, which is being handed a file the writer has not
    /// finished writing.
    /// </remarks>
    internal async Task<bool> AddAsync(string path, CancellationToken cancellationToken)
    {
        if (!WatchFolder.ShouldAdd(path))
        {
            return true;
        }

        await _addLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The sweep and watcher can both see a newly arrived file. Whichever one gets here
            // second finds that the first has already renamed it and has nothing left to do.
            if (!File.Exists(path))
            {
                return true;
            }

            await _torrentService.AddTorrentFileAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false);
            File.Move(path, WatchFolder.MarkedPath(path), overwrite: true);
            _logger.LogInformation("Added {File} from the watched folder", Path.GetFileName(path));
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not add {File} from the watched folder", Path.GetFileName(path));
            return false;
        }
        finally
        {
            _addLock.Release();
        }
    }

    /// <summary>
    /// Retries a file that may still be open or incomplete when the file-system event arrives.
    /// </summary>
    internal async Task AddWithRetriesAsync(
        string path,
        CancellationToken cancellationToken,
        TimeSpan? retryDelay = null)
    {
        var delay = retryDelay ?? RetryDelay;
        for (var attempt = 1; attempt <= AddAttempts; attempt++)
        {
            if (await AddAsync(path, cancellationToken).ConfigureAwait(false)
                || attempt == AddAttempts
                || !File.Exists(path))
            {
                return;
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async void OnAppeared(object sender, FileSystemEventArgs e)
    {
        try
        {
            // A file is reported the moment it is created, which can be before whatever is writing it
            // has finished. Failing is handled - the file stays for the next sweep - but waiting a
            // moment first turns the common case into a success rather than a retry.
            await Task.Delay(RetryDelay, _stopping.Token).ConfigureAwait(false);
            await AddWithRetriesAsync(e.FullPath, _stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // Nothing above this: an exception out of an event handler on a background thread ends
            // the process.
            _logger.LogWarning(ex, "The watched folder handler failed");
        }
    }
}
