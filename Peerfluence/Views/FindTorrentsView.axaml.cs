using Avalonia.Controls;
using Avalonia.Input;
using Peerfluence.ViewModels;

namespace Peerfluence.Views;

public partial class FindTorrentsView : UserControl
{
    public FindTorrentsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => (DataContext as FindTorrentsViewModel)?.Refresh();
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
