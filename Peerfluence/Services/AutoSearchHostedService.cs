using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Peerfluence.Core.Services;
using PeerSharp.Config;

namespace Peerfluence.Services;

/// <summary>
/// Runs the saved query on a timer and adds what it has not seen before.
/// </summary>
/// <remarks>
/// <para>
/// Every decision lives in <see cref="AutoSearch"/>. This waits, asks, adds, and writes down what it
/// added so the next run does not add it again.
/// </para>
/// <para>
/// A failure is left alone rather than retried faster: the search service already treats an
/// unreachable indexer as an ordinary outcome, and the next tick is soon enough.
/// </para>
/// </remarks>
internal sealed class AutoSearchHostedService : BackgroundService
{
    private readonly IAppSettingsService _settingsService;
    private readonly ITorrentSearchService _searchService;
    private readonly ITorrentService _torrentService;
    private readonly ITorrentCategoryService _categoryService;
    private readonly ILogger<AutoSearchHostedService> _logger;

    public AutoSearchHostedService(
        IAppSettingsService settingsService,
        ITorrentSearchService searchService,
        ITorrentService torrentService,
        ITorrentCategoryService categoryService,
        ILogger<AutoSearchHostedService> logger)
    {
        _settingsService = settingsService;
        _searchService = searchService;
        _torrentService = torrentService;
        _categoryService = categoryService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Read the interval each time round, so changing it in the settings takes effect at
                // the next tick rather than at the next restart.
                await Task.Delay(AutoSearch.Interval(_settingsService.Current.AutoSearch), stoppingToken)
                    .ConfigureAwait(false);

                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <summary>Runs the saved query once and adds whatever is new.</summary>
    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = _settingsService.Current.AutoSearch;

        if (!AutoSearch.IsRunnable(settings) || !_searchService.IsConfigured)
        {
            return;
        }

        var response = await _searchService.SearchAsync(settings.Query, cancellationToken).ConfigureAwait(false);
        if (response.Failure != SearchFailure.None)
        {
            _logger.LogInformation("The automatic search did not run: {Failure}", response.Failure);
            return;
        }

        var added = 0;
        foreach (var result in AutoSearch.NewResults(settings, response.Results))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (await AddAsync(result, settings.Category, cancellationToken).ConfigureAwait(false))
            {
                AutoSearch.Remember(settings, result.Link);
                added++;
            }
        }

        if (added > 0)
        {
            await _settingsService.SaveAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("The automatic search added {Count} torrents", added);
        }
    }

    private async Task<bool> AddAsync(TorrentSearchResult result, string category, CancellationToken cancellationToken)
    {
        try
        {
            var torrent = result.IsMagnet
                ? await _torrentService.AddMagnetAsync(result.Link, cancellationToken: cancellationToken).ConfigureAwait(false)
                : await _torrentService.AddTorrentFromUrlAsync(result.Link, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(category))
            {
                // The hash it actually has: a v2 only torrent stores InfoHash.Empty as its v1 hash,
                // and filing something under the empty hash files nothing.
                var hash = torrent.Hash.IsEmpty ? torrent.HashV2 : torrent.Hash;
                await _categoryService.AssignAsync(hash, category, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception ex)
        {
            // Left unremembered, so a result that failed for a passing reason is tried again.
            _logger.LogWarning(ex, "The automatic search could not add {Title}", result.Title);
            return false;
        }
    }
}
