using SukiUI.Controls;
using Peerfluence.ViewModels;
using System;

namespace Peerfluence.Views;

public partial class MainWindowView : SukiWindow
{
    public MainWindowView()
    {
        InitializeComponent();
    }

    public MainWindowView(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;

        Hosts.Add(new SukiToastHost { Manager = viewModel.ToastManager });
        Hosts.Add(new SukiDialogHost { Manager = viewModel.DialogManager });
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
