using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Peerfluence.ViewModels;

namespace Peerfluence.Views;

public partial class FindTorrentsView : UserControl
{
    public FindTorrentsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => (DataContext as FindTorrentsViewModel)?.Refresh();

        // Tunnelling, because the grid takes Enter for itself to move down a row, and a handled
        // event never bubbles back up to here.
        ResultsGrid.AddHandler(KeyDownEvent, ResultsGrid_OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Enter adds the selected result, so the results can be worked through without a mouse.
    /// </summary>
    private void ResultsGrid_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter
            && e.KeyModifiers == KeyModifiers.None
            && DataContext is FindTorrentsViewModel viewModel
            && viewModel.AddCommand.CanExecute(viewModel.SelectedResult))
        {
            viewModel.AddCommand.Execute(viewModel.SelectedResult);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Double-click adds, as it does in the downloads list. The row under the pointer is already
    /// the selected one by the time this fires.
    /// </summary>
    private void ResultsGrid_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is FindTorrentsViewModel viewModel && viewModel.SelectedResult != null)
        {
            viewModel.AddCommand.Execute(viewModel.SelectedResult);
        }
    }
}
