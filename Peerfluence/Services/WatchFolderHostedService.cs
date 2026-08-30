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
    private readonly IAppSettingsService _settingsService;
    private readonly ITorrentService _torrentService;
    private readonly ILogger<WatchFolderHostedService> _logger;

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
        var settings = _settingsService.Current.WatchFolder;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Path) || !Directory.Exists(settings.Path))
        {
            return;
        }

        await SweepAsync(settings.Path, cancellationToken).ConfigureAwait(false);

        _watcher = new FileSystemWatcher(settings.Path, "*.torrent")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        _watcher.Created += OnAppeared;
        _watcher.Renamed += OnAppeared;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_watcher is not null)
        {
            _watcher.Created -= OnAppeared;
            _watcher.Renamed -= OnAppeared;
            _watcher.Dispose();
            _watcher = null;
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
    internal async Task AddAsync(string path, CancellationToken cancellationToken)
    {
        if (!WatchFolder.ShouldAdd(path))
        {
            return;
        }

        try
        {
            await _torrentService.AddTorrentFileAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false);
            File.Move(path, WatchFolder.MarkedPath(path), overwrite: true);
            _logger.LogInformation("Added {File} from the watched folder", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not add {File} from the watched folder", Path.GetFileName(path));
        }
    }

    private async void OnAppeared(object sender, FileSystemEventArgs e)
    {
        try
        {
            // A file is reported the moment it is created, which can be before whatever is writing it
            // has finished. Failing is handled - the file stays for the next sweep - but waiting a
            // moment first turns the common case into a success rather than a retry.
            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
            await AddAsync(e.FullPath, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Nothing above this: an exception out of an event handler on a background thread ends
            // the process.
            _logger.LogWarning(ex, "The watched folder handler failed");
        }
    }
}
