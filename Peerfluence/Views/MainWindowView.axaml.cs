using System;
using Avalonia.Controls;
using Peerfluence.ViewModels;

namespace Peerfluence.Views;

public partial class MainWindowView : Window
{
    public MainWindowView()
    {
        InitializeComponent();
    }

    public MainWindowView(MainWindowViewModel viewModel) : this()
    {
        // The toast and dialog hosts bind to the view model's managers in the markup, so there is
        // nothing to attach here any more. The previous window type had a Hosts collection of its
        // own, which is what this used to fill.
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }
}
