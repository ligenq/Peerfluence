using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.ViewModels;
using Peerfluence.Views;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.HeadlessTests;

/// <summary>
/// Simple mode's list, rendered.
/// </summary>
public class SimpleDownloadsViewTests
{
    private static (SimpleDownloadsView View, Window Window, DownloadsViewModel Vm, TorrentListItemViewModel Row) ShowOneRow()
    {
        var vm = TestHelpers.CreateDownloadsViewModel();
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns("ubuntu-24.04.iso");
        torrent.Hash.Returns(InfoHash.CreateRandom());
        torrent.HashV2.Returns(InfoHash.EmptyV2);
        torrent.Started.Returns(true);
        var row = new TorrentListItemViewModel(torrent) { Actions = vm };
        vm.Torrents.Add(row);

        var view = new SimpleDownloadsView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        return (view, window, vm, row);
    }

    private static Button ButtonWithId(Control view, string id)
    {
        return view.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetAutomationId(button) == id);
    }

    /// <summary>
    /// Icon buttons with a tooltip and nothing else gave a screen reader nothing to read, and the
    /// pause button's tooltip said "Pause or resume" whichever it was about to do.
    /// </summary>
    [AvaloniaFact]
    public void TheRowsButtons_SayWhatTheyWillDo_AndToWhich()
    {
        var (view, window, _, row) = ShowOneRow();

        try
        {
            Assert.Equal(row.ToggleLabel, AutomationProperties.GetName(ButtonWithId(view, "ToggleTorrentButton")));
            Assert.Contains("ubuntu-24.04.iso", row.ToggleLabel, StringComparison.Ordinal);
            Assert.Equal(row.OpenFolderLabel, AutomationProperties.GetName(ButtonWithId(view, "OpenTorrentFolderButton")));
            Assert.Equal(row.MoreActionsLabel, AutomationProperties.GetName(ButtonWithId(view, "TorrentRowMenuButton")));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Remove used to be in a right-click menu and nowhere else: out of sight, and out of reach of
    /// the keyboard. The row's menu button is both seen and a tab stop.
    /// </summary>
    [AvaloniaFact]
    public void Remove_IsReachableFromAButtonOnTheRow()
    {
        var (view, window, vm, row) = ShowOneRow();

        try
        {
            var menuButton = ButtonWithId(view, "TorrentRowMenuButton");
            Assert.True(menuButton.IsEffectivelyVisible);
            Assert.True(menuButton.Focusable);
            Assert.True(menuButton.IsTabStop);

            var flyout = Assert.IsType<MenuFlyout>(menuButton.Flyout);
            flyout.ShowAt(menuButton);
            Dispatcher.UIThread.RunJobs();

            try
            {
                var remove = flyout.Items.OfType<MenuItem>()
                    .Single(item => AutomationProperties.GetAutomationId(item) == "RemoveTorrentMenuItem");
                Assert.Same(vm.RemoveTorrentCommand, remove.Command);
                Assert.Same(row, remove.CommandParameter);
            }
            finally
            {
                flyout.Hide();
            }
        }
        finally
        {
            window.Close();
        }
    }
}
