using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Peerfluence.ViewModels;
using SukiUI.Controls;

namespace Peerfluence.Views;

public partial class DownloadsView : UserControl
{
    public DownloadsView()
    {
        InitializeComponent();
        var statusInfoBar = this.FindControl<InfoBar>("DownloadsStatusInfoBar");
        if (statusInfoBar != null)
        {
            statusInfoBar.PropertyChanged += DownloadsStatusInfoBar_OnPropertyChanged;
        }
    }

    private void TorrentDataGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid dataGrid || DataContext is not DownloadsViewModel viewModel)
        {
            return;
        }

        viewModel.SelectedTorrent = dataGrid.SelectedItem as TorrentListItemViewModel;
    }

    private void TorrentDataGrid_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not DownloadsViewModel viewModel)
        {
            return;
        }

        var torrent = TryGetTorrentFromEventSource(e.Source);
        if (torrent != null)
        {
            viewModel.SelectedTorrent = torrent;
        }
    }

    private void DownloadsStatusInfoBar_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty
            && sender is Control { IsVisible: false }
            && DataContext is DownloadsViewModel { HasStatusMessage: true } viewModel)
        {
            viewModel.ClearStatusCommand.Execute(null);
        }
    }

    internal static TorrentListItemViewModel? TryGetTorrentFromEventSource(object? source)
    {
        var current = source as Control;
        while (current != null && current is not DataGridRow)
        {
            current = current.Parent as Control;
        }

        return (current as DataGridRow)?.DataContext as TorrentListItemViewModel;
    }
}
