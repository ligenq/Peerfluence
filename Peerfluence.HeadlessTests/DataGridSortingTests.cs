using System.Globalization;
using System.Net;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Peerfluence.Core.Services;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.ViewModels;
using Peerfluence.Views;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.HeadlessTests;

public sealed class DataGridSortingTests
{
    [AvaloniaTheory]
    [InlineData("Downloads")]
    [InlineData("Search")]
    [InlineData("Files")]
    [InlineData("AddFiles")]
    [InlineData("Trackers")]
    [InlineData("Peers")]
    [InlineData("WebSeeds")]
    public void DataColumnHeaders_SortBothDirectionsByTheUnderlyingValues(string scenario)
    {
        var (source, first, second) = CreateScenario(scenario);
        var columns = source.Columns.ToArray();
        source.Columns.Clear();
        try
        {
            foreach (var column in columns.Where(c => c.Header is not null))
            {
                Assert.True(column.CanUserSort);
                Assert.NotNull(column.CustomSortComparer);
                column.Width = new DataGridLength(300);
                var items = new[] { second, first };
                var grid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    IsReadOnly = true,
                    ItemsSource = items,
                    SelectedItem = second,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                };
                grid.Columns.Add(column);
                var window = new Window { Content = grid, Width = 500, Height = 250 };
                window.Show();
                try
                {
                    Layout(window);
                    var header = Assert.Single(grid.GetVisualDescendants().OfType<DataGridColumnHeader>(),
                        h => Equals(h.Content, column.Header));
                    Click(window, header);
                    Assert.Equal(new[] { first, second }, grid.CollectionView.Cast<object>());
                    Click(window, header);
                    Assert.Equal(new[] { second, first }, grid.CollectionView.Cast<object>());
                    Assert.Same(second, grid.SelectedItem);
                    Assert.Same(second, items[0]);
                }
                finally
                {
                    window.Close();
                    grid.Columns.Clear();
                }
            }

            foreach (var column in columns.Where(c => c.Header is null))
            {
                Assert.False(column.CanUserSort);
                Assert.Null(column.CustomSortComparer);
            }
        }
        finally
        {
            (first as TorrentListItemViewModel)?.Detach();
            (second as TorrentListItemViewModel)?.Detach();
        }
    }

    [AvaloniaFact]
    public void EtaSort_PlacesCompletedThenFiniteThenUnknownEstimates()
    {
        var complete = Torrent("Complete", 0, 0);
        var shortWait = Torrent("Two minutes", 120 * 1024, 1024);
        var longWait = Torrent("One hour", 3600 * 1024, 1024);
        var unknown = Torrent("Unknown", 1024, 0);
        try
        {
            var comparer = Controls.DataGridComparers.TorrentEta;
            Assert.True(comparer.Compare(complete, shortWait) < 0);
            Assert.True(comparer.Compare(shortWait, longWait) < 0);
            Assert.True(comparer.Compare(longWait, unknown) < 0);
            unknown.DownloadSpeedBytesPerSecond = 1024;
            Assert.True(comparer.Compare(unknown, shortWait) < 0);
        }
        finally
        {
            foreach (var item in new[] { complete, shortWait, longWait, unknown })
            {
                item.Detach();
            }
        }
    }

    [AvaloniaFact]
    public void AgeAndProgressSort_DistinguishMissingValuesFromRealValues()
    {
        var recent = Search("Recent", DateTimeOffset.UtcNow.AddDays(-2));
        var old = Search("Old", DateTimeOffset.UtcNow.AddDays(-10));
        var unknown = Search("Unknown", null);
        var future = Search("Future", DateTimeOffset.UtcNow.AddDays(2));
        var age = Controls.DataGridComparers.SearchAge;
        Assert.True(age.Compare(recent, old) < 0);
        Assert.True(age.Compare(old, unknown) < 0);
        Assert.Equal(0, age.Compare(unknown, future));

        var silentPeer = Peer("127.0.0.1", "", "", 0, 0, false);
        var emptyPeer = Peer("127.0.0.2", "", "", 0, 0, true);
        Assert.True(Controls.DataGridComparers.PeerProgress.Compare(silentPeer, emptyPeer) < 0);
    }

    /// <summary>
    /// Text sorts the way the reader's language does, including after the language changes.
    /// </summary>
    /// <remarks>
    /// The comparers are built once, when a view's <c>{x:Static}</c> column reference is first
    /// resolved, but the language is a setting. A cached
    /// <see cref="StringComparer.CurrentCultureIgnoreCase"/> would hold the collation from startup,
    /// so a reader who switched to Swedish would keep English ordering for the whole session.
    /// Swedish is the useful case to pin: it sorts the three extra vowels after Z, where English
    /// treats them as accented A and O.
    /// </remarks>
    [AvaloniaFact]
    public void TextSort_FollowsTheCurrentCultureWhenTheLanguageChanges()
    {
        var zulu = Torrent("Zulu", 0, 0);
        var angstrom = Torrent("Ångström", 0, 0);
        var original = CultureInfo.CurrentCulture;
        try
        {
            var comparer = Controls.DataGridComparers.TorrentName;

            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.True(comparer.Compare(angstrom, zulu) < 0);

            // Same comparer instance, different answer: the collation is read per comparison.
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.True(comparer.Compare(angstrom, zulu) > 0);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            zulu.Detach();
            angstrom.Detach();
        }
    }

    private static (DataGrid Grid, object First, object Second) CreateScenario(string scenario)
    {
        const long small = 900 * 1024;
        const long large = 1024 * 1024;
        switch (scenario)
        {
            case "Downloads":
                var first = Torrent("alpha", 120 * small, small);
                var second = Torrent("Zeta", 3600 * large, large);
                first.TotalSizeBytes = small;
                second.TotalSizeBytes = large;
                first.Progress = 0.09f;
                second.Progress = 1;
                first.Category = "alpha";
                second.Category = "Zeta";
                first.State = "Downloading";
                second.State = "Seeding";
                first.UploadSpeedBytesPerSecond = small;
                second.UploadSpeedBytesPerSecond = large;
                first.ConnectedPeers = 2;
                second.ConnectedPeers = 10;
                return (new DownloadsView().FindControl<DataGrid>("TorrentDataGrid")!, first, second);
            case "Search":
                return (new FindTorrentsView().FindControl<DataGrid>("ResultsGrid")!,
                    new TorrentSearchResultViewModel(new TorrentSearchResult("alpha", small, 2, 2, "alpha", DateTimeOffset.UtcNow.AddDays(-2), "magnet:?a")),
                    new TorrentSearchResultViewModel(new TorrentSearchResult("Zeta", large, 10, 10, "Zeta", DateTimeOffset.UtcNow.AddDays(-10), "magnet:?b")));
            case "Files":
                return (DetailsGrid("DetailsFilesGrid"),
                    new TorrentFileItemViewModel(new TorrentFileInfo("alpha", small, 0, 2), new FileSelection(false, Priority.Low)),
                    new TorrentFileItemViewModel(new TorrentFileInfo("Zeta", large, 1, large), new FileSelection(true, Priority.High)));
            case "AddFiles":
                var dialog = new AddTorrentOptionsWindow();
                var grid = Assert.Single(dialog.GetLogicalDescendants().OfType<DataGrid>());
                var low = new AddTorrentFileOptionViewModel(0, "alpha", small) { IsSelected = false };
                var high = new AddTorrentFileOptionViewModel(1, "Zeta", large) { Priority = Priority.High };
                return (grid, low, high);
            case "Trackers":
                return (DetailsGrid("TrackersGrid"),
                    new TrackerStatusItemViewModel(new TrackerStatus("https://alpha.invalid", TrackerStatusType.Disabled, SeedCount: 2, LeechCount: 2, LastError: "alpha")),
                    new TrackerStatusItemViewModel(new TrackerStatus("https://zeta.invalid", TrackerStatusType.Working, SeedCount: 10, LeechCount: 10, LastError: "Zeta")));
            case "Peers":
                return (DetailsGrid("PeersGrid"), Peer("127.0.0.1", "alpha", "AU", small, 0.09f, true),
                    Peer("127.0.0.2", "Zeta", "SE", large, 1, true));
            case "WebSeeds":
                return (DetailsGrid("WebSeedsGrid"), "https://alpha.invalid", "https://zeta.invalid");
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }

    private static DataGrid DetailsGrid(string id) => new DetailsView().GetLogicalDescendants()
        .OfType<DataGrid>().Single(grid => AutomationProperties.GetAutomationId(grid) == id);

    private static TorrentListItemViewModel Torrent(string name, long remaining, long speed)
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns(name);
        torrent.Hash.Returns(InfoHash.CreateRandom());
        torrent.HashV2.Returns(InfoHash.EmptyV2);
        return new TorrentListItemViewModel(torrent) { DataLeftBytes = remaining, DownloadSpeedBytesPerSecond = speed };
    }

    private static TorrentSearchResultViewModel Search(string title, DateTimeOffset? date) =>
        new(new TorrentSearchResult(title, 0, 0, 0, "Indexer", date, "magnet:?a"));

    private static PeerInfoItemViewModel Peer(string address, string client, string country, long speed, float progress, bool reported) =>
        new(new PeerInfo(new IPEndPoint(IPAddress.Parse(address), 6881), Country: country, ClientName: client,
            DownloadSpeed: speed, UploadSpeed: speed, Progress: progress)
        { HasReportedPieces = reported });

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Layout(window);
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
