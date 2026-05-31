using SukiUI.Controls;
using Peerfluence.ViewModels;

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
}
