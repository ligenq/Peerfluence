using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Peerfluence.ViewModels;

namespace Peerfluence.Controls;

/// <summary>
/// Typed column comparisons avoid reflection in NativeAOT and sort formatted cells by their values.
/// </summary>
public static class DataGridComparers
{
    public static IComparer TorrentName { get; } = Text<TorrentListItemViewModel>(row => row.Name);
    public static IComparer TorrentProgress { get; } = Value<TorrentListItemViewModel, float>(row => row.Progress);
    public static IComparer TorrentSize { get; } = Value<TorrentListItemViewModel, long>(row => row.TotalSizeBytes);
    public static IComparer TorrentCategory { get; } = Text<TorrentListItemViewModel>(row => row.Category);
    public static IComparer TorrentState { get; } = Text<TorrentListItemViewModel>(row => row.State);
    public static IComparer TorrentEta { get; } = Value<TorrentListItemViewModel, double>(row => row.EstimatedSecondsRemaining);
    public static IComparer TorrentDownloadSpeed { get; } = Value<TorrentListItemViewModel, long>(row => row.DownloadSpeedBytesPerSecond);
    public static IComparer TorrentUploadSpeed { get; } = Value<TorrentListItemViewModel, long>(row => row.UploadSpeedBytesPerSecond);
    public static IComparer TorrentPeers { get; } = Value<TorrentListItemViewModel, int>(row => row.ConnectedPeers);

    public static IComparer SearchTitle { get; } = Text<TorrentSearchResultViewModel>(row => row.Title);
    public static IComparer SearchSize { get; } = Value<TorrentSearchResultViewModel, long>(row => row.SizeBytes);
    public static IComparer SearchSeeds { get; } = Value<TorrentSearchResultViewModel, int>(row => row.Seeders);
    public static IComparer SearchPeers { get; } = Value<TorrentSearchResultViewModel, int>(row => row.Peers);
    public static IComparer SearchIndexer { get; } = Text<TorrentSearchResultViewModel>(row => row.IndexerName);
    // Ascending age means newest first. Missing/future timestamps have no displayed age and go last.
    public static IComparer SearchAge { get; } = Value<TorrentSearchResultViewModel, long>(row =>
        row.PublishedAt is { } published && published <= DateTimeOffset.UtcNow ? -published.UtcTicks : long.MaxValue);

    public static IComparer FileSelected { get; } = Value<TorrentFileItemViewModel, bool>(row => row.IsSelected);
    public static IComparer FilePriority { get; } = Value<TorrentFileItemViewModel, Priority>(row => row.Priority);
    public static IComparer FilePath { get; } = Text<TorrentFileItemViewModel>(row => row.Path);
    public static IComparer FileSize { get; } = Value<TorrentFileItemViewModel, long>(row => row.SizeBytes);
    public static IComparer FileDownloaded { get; } = Value<TorrentFileItemViewModel, long>(row => row.DownloadedBytes);
    public static IComparer FileProgress { get; } = Value<TorrentFileItemViewModel, float>(row => row.Progress);

    public static IComparer AddFileSelected { get; } = Value<AddTorrentFileOptionViewModel, bool>(row => row.IsSelected);
    public static IComparer AddFilePath { get; } = Text<AddTorrentFileOptionViewModel>(row => row.Path);
    public static IComparer AddFileSize { get; } = Value<AddTorrentFileOptionViewModel, long>(row => row.SizeBytes);
    public static IComparer AddFilePriority { get; } = Value<AddTorrentFileOptionViewModel, Priority>(row => row.Priority);

    public static IComparer TrackerUrl { get; } = Text<TrackerStatusItemViewModel>(row => row.Url);
    public static IComparer TrackerState { get; } = Text<TrackerStatusItemViewModel>(row => row.State);
    public static IComparer TrackerSeeds { get; } = Value<TrackerStatusItemViewModel, uint>(row => row.SeedCount);
    public static IComparer TrackerLeeches { get; } = Value<TrackerStatusItemViewModel, uint>(row => row.LeechCount);
    public static IComparer TrackerError { get; } = Text<TrackerStatusItemViewModel>(row => row.LastError);

    public static IComparer PeerEndpoint { get; } = Text<PeerInfoItemViewModel>(row => row.EndPoint);
    public static IComparer PeerClient { get; } = Text<PeerInfoItemViewModel>(row => row.Client);
    public static IComparer PeerCountry { get; } = Text<PeerInfoItemViewModel>(row => row.Country);
    public static IComparer PeerDownloadSpeed { get; } = Value<PeerInfoItemViewModel, long>(row => row.DownloadSpeedBytesPerSecond);
    public static IComparer PeerUploadSpeed { get; } = Value<PeerInfoItemViewModel, long>(row => row.UploadSpeedBytesPerSecond);
    public static IComparer PeerProgress { get; } = Value<PeerInfoItemViewModel, float>(row => row.HasReportedPieces ? row.Progress : -1);

    public static IComparer WebSeedUrl { get; } = Text<string>(url => url);

    /// <summary>
    /// Compares as the reader's language does, resolving the collation on every comparison.
    /// </summary>
    /// <remarks>
    /// Deliberately not a cached <see cref="StringComparer.CurrentCultureIgnoreCase"/>. These
    /// comparers are built once, when a view's <c>{x:Static}</c> column reference is first
    /// resolved, but the language is a setting: <c>LocalizationService.Apply</c> reassigns
    /// <see cref="CultureInfo.CurrentCulture"/> whenever it changes. A comparer captured at class
    /// load would keep the collation from startup, so a reader who switched to Swedish would still
    /// get the three vowels that belong after Z sorted as accented A and O, for the rest of the
    /// session.
    /// <see cref="CompareInfo"/> is cached on the culture, so reading it per call allocates nothing.
    /// </remarks>
    private static IComparer Text<T>(Func<T, string> selector) =>
        Comparer<T>.Create((left, right) => CultureInfo.CurrentCulture.CompareInfo
            .Compare(selector(left!), selector(right!), CompareOptions.IgnoreCase));

    private static IComparer Value<T, TValue>(Func<T, TValue> selector) =>
        Comparer<T>.Create((left, right) => Comparer<TValue>.Default.Compare(selector(left!), selector(right!)));
}
