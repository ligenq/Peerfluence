using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Core.Messaging;
using Peerfluence.Core.Services;
using Peerfluence.Properties;
using PeerSharp.Config;

namespace Peerfluence.ViewModels;

[SingletonService]
public sealed class FindTorrentsViewModel : ViewModelBase, IFeatureViewModel
{
    private readonly ITorrentSearchService _searchService;
    private readonly ITorrentService _torrentService;
    private readonly IAddTorrentDialogService _addTorrentDialogService;
    private CancellationTokenSource? _searchCts;

    public FindTorrentsViewModel(
        ITorrentSearchService searchService,
        ITorrentService torrentService,
        IAddTorrentDialogService addTorrentDialogService)
    {
        _searchService = searchService;
        _torrentService = torrentService;
        _addTorrentDialogService = addTorrentDialogService;

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !string.IsNullOrWhiteSpace(Query));
        AddCommand = new AsyncRelayCommand<TorrentSearchResultViewModel?>(AddAsync);
        OpenSearchSettingsCommand = new RelayCommand(
            () => WeakReferenceMessenger.Default.Send(new ShowSearchSettingsMessage()));
    }

    // IFeatureViewModel
    public string Title => Resources.Nav_FindTorrents;

    public string IconKind => "Magnify";

    public int Order => 50;

    public ObservableCollection<TorrentSearchResultViewModel> Results { get; } = new();

    public string Query
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                SearchCommand.NotifyCanExecuteChanged();
            }
        }
    } = string.Empty;

    public TorrentSearchResultViewModel? SelectedResult
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool IsSearching
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Whether an endpoint has been configured. Re-read on every visit rather than cached, because
    /// settings is where it gets configured and the user walks straight back here afterwards.
    /// </summary>
    public bool IsConfigured
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool IsNotConfigured => !IsConfigured;

    /// <summary>
    /// What happened, when it is worth saying: a failure, a partial result, or nothing found.
    /// Empty when the last search simply worked.
    /// </summary>
    public string StatusMessage
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    } = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    /// <summary>
    /// Whether the thing that went wrong is one the settings could fix, so the offer to go there is
    /// only made when it would actually help.
    /// </summary>
    public bool CanFixInSettings
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>
    /// The way out of both dead ends on this screen: never having set up an indexer, and having one
    /// that is not answering.
    /// </summary>
    public IRelayCommand OpenSearchSettingsCommand { get; }

    public IAsyncRelayCommand SearchCommand { get; }

    public IAsyncRelayCommand<TorrentSearchResultViewModel?> AddCommand { get; }

    /// <summary>
    /// Called when the screen is shown, so an endpoint configured a moment ago takes effect without
    /// a restart.
    /// </summary>
    public void Refresh()
    {
        IsConfigured = _searchService.IsConfigured;
        OnPropertyChanged(nameof(IsNotConfigured));
    }

    private async Task SearchAsync()
    {
        Refresh();
        if (!IsConfigured)
        {
            return;
        }

        // A new search replaces the one in flight rather than racing it.
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchCts = cts;

        IsSearching = true;
        StatusMessage = string.Empty;
        CanFixInSettings = false;

        try
        {
            var response = await _searchService.SearchAsync(Query, cts.Token).ConfigureAwait(true);
            if (cts.Token.IsCancellationRequested)
            {
                return;
            }

            Results.Clear();

            // Seeds descending: on an aggregated search it is the only quality signal the feed
            // carries, and the ones nobody is seeding are the ones nobody wants.
            foreach (var result in response.Results.OrderByDescending(r => r.Seeders))
            {
                Results.Add(new TorrentSearchResultViewModel(result));
            }

            StatusMessage = DescribeOutcome(response);
            CanFixInSettings = response.IsSettingsFixable;
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                IsSearching = false;
            }
        }
    }

    private static string DescribeOutcome(TorrentSearchResponse response)
    {
        if (response.HasFailure)
        {
            return Describe(response);
        }

        // Said plainly rather than hidden: an empty list because two indexers timed out is a
        // different problem from an empty list because nothing matched.
        if (response.IsPartial)
        {
            var responded = response.IndexersQueried - response.IndexersFailed;
            return string.Format(Resources.Find_PartialResults, responded, response.IndexersQueried);
        }

        return response.Results.Count == 0 ? Resources.Find_NoResults : string.Empty;
    }

    /// <summary>
    /// Says what went wrong in terms of what the user did, not in terms of what the socket did.
    ///
    /// <para>
    /// The case that matters is Unreachable. Pressing "Use Jackett" writes an address for software
    /// the user may never have installed, so the overwhelmingly common failure is not a broken
    /// setup - it is a setup that was never finished, reported by Windows as "the target machine
    /// actively refused it". That sentence tells someone nothing they can act on.
    /// </para>
    /// </summary>
    private static string Describe(TorrentSearchResponse response)
    {
        return response.Failure switch
        {
            SearchFailure.NotConfigured => Resources.Find_Failure_NotConfigured,
            SearchFailure.Unreachable => string.Format(
                Resources.Find_Failure_Unreachable,
                response.FailureDetail ?? string.Empty),
            SearchFailure.Rejected => Resources.Find_Failure_Rejected,
            SearchFailure.NotTorznab => Resources.Find_Failure_NotTorznab,
            SearchFailure.RateLimited => string.Format(
                Resources.Find_Failure_RateLimited,
                response.FailureDetail ?? string.Empty),
            _ => string.Format(Resources.Find_SearchFailed, response.FailureDetail ?? string.Empty)
        };
    }

    private async Task AddAsync(TorrentSearchResultViewModel? result)
    {
        if (result == null)
        {
            return;
        }

        try
        {
            // Through the same dialog as every other add, so the download path and file selection
            // are asked for in the one place that asks for them.
            if (result.IsMagnet)
            {
                await _addTorrentDialogService.ShowMagnetAsync(result.Link);
                return;
            }

            // A search result is never a file on this machine. Sources that carry a link point at a
            // .torrent on someone's server, and the engine's file loader only reads local paths -
            // handing it a URL failed with "torrent file not found", which is true and useless.
            await _torrentService.AddTorrentFromUrlAsync(result.Link, new AddTorrentOptions());
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Resources.Find_AddFailed, ex.Message);
            CanFixInSettings = false;
        }
    }
}
