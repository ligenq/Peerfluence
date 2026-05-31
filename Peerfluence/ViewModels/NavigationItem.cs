using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace Peerfluence.ViewModels;

public sealed class NavigationItem : ObservableObject
{
    public NavigationItem(string title, MaterialIconKind icon, ViewModelBase viewModel)
    {
        Title = title;
        Icon = icon;
        ViewModel = viewModel;
    }

    public string Title
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public MaterialIconKind Icon { get; }

    public ViewModelBase ViewModel { get; }
}
