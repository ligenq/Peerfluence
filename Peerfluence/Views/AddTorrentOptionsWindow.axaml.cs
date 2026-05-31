using System;
using Avalonia.Markup.Xaml;
using Peerfluence.ViewModels;
using SukiUI.Controls;

namespace Peerfluence.Views;

public partial class AddTorrentOptionsWindow : SukiWindow
{
    private AddTorrentOptionsViewModel? _currentViewModel;

    public AddTorrentOptionsWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _currentViewModel?.OnRequestClose -= Close;

        _currentViewModel = DataContext as AddTorrentOptionsViewModel;
        _currentViewModel?.OnRequestClose += Close;
    }

    protected override void OnClosed(EventArgs e)
    {
        _currentViewModel?.OnRequestClose -= Close;
        base.OnClosed(e);
    }
}
