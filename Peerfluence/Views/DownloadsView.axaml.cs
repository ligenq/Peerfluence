using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Peerfluence.ViewModels;
using SukiUI.Controls;

namespace Peerfluence.Views;

public partial class DownloadsView : UserControl
{
    /// <summary>
    /// How much of the window the details pane takes when open, against the list's own 2*.
    /// </summary>
    private static readonly GridLength DetailsPaneHeight = new(2, GridUnitType.Star);

    private static readonly GridLength SplitterHeight = new(8, GridUnitType.Pixel);

    private static readonly GridLength Collapsed = new(0, GridUnitType.Pixel);

    public DownloadsView()
    {
        InitializeComponent();
        var statusInfoBar = this.FindControl<InfoBar>("DownloadsStatusInfoBar");
        if (statusInfoBar != null)
        {
            statusInfoBar.PropertyChanged += DownloadsStatusInfoBar_OnPropertyChanged;
        }

        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Gives the details pane's rows their height, or takes it away entirely.
    ///
    /// <para>
    /// A star-sized row goes on reserving its share whether or not the control in it is visible, so
    /// this cannot be left to <c>IsVisible</c>: with the pane closed the list would still stop
    /// halfway down the window. Row heights are not bindable from XAML - a RowDefinition is outside
    /// the logical tree and inherits no DataContext - so the view sets them itself.
    /// </para>
    /// </summary>
    private void ApplyDetailsPaneLayout(bool isVisible)
    {
        var rows = ContentGrid.RowDefinitions;
        rows[4].Height = isVisible ? SplitterHeight : Collapsed;
        rows[5].Height = isVisible ? DetailsPaneHeight : Collapsed;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is not DownloadsViewModel viewModel)
        {
            return;
        }

        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyDetailsPaneLayout(viewModel.IsDetailsPaneVisible);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadsViewModel.IsDetailsPaneVisible)
            && sender is DownloadsViewModel viewModel)
        {
            ApplyDetailsPaneLayout(viewModel.IsDetailsPaneVisible);
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
