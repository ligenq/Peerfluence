using Avalonia.Markup.Xaml;
using SukiUI.Controls;
using Peerfluence.ViewModels;
using System;

namespace Peerfluence.Views;

public partial class CreateTorrentWindow : SukiWindow
{
    private CreateTorrentViewModel? _currentViewModel;

    public CreateTorrentWindow()
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

        _currentViewModel = DataContext as CreateTorrentViewModel;
        _currentViewModel?.OnRequestClose += Close;
    }

    protected override void OnClosed(EventArgs e)
    {
        _currentViewModel?.OnRequestClose -= Close;

        base.OnClosed(e);
    }
}
