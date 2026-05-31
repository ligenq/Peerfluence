using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Material.Icons;
using Peerfluence.Core.Messaging;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace Peerfluence.ViewModels;

[SingletonService]
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly AboutViewModel _aboutViewModel;
    private readonly List<NavigationItem> _featureItems = new();

    public MainWindowViewModel(
        IEnumerable<IFeatureViewModel> features,
        AboutViewModel aboutViewModel,
        INotificationService notificationService)
    {
        _aboutViewModel = aboutViewModel;

        // Create SukiUI managers here (after UI thread is available)
        ToastManager = new SukiToastManager();
        DialogManager = new SukiDialogManager();

        // Wire toast manager into notification service
        if (notificationService is NotificationService ns)
        {
            ns.ToastManager = ToastManager;
        }

        // Build navigation from discovered features
        foreach (var feature in features.OrderBy(f => f.Order))
        {
            var icon = Enum.TryParse<MaterialIconKind>(feature.IconKind, out var parsed)
                ? parsed
                : MaterialIconKind.CircleOutline;

            var item = new NavigationItem(feature.Title, icon, (ViewModelBase)feature);
            _featureItems.Add(item);

            // Wire dialog manager into downloads view model
            if (feature is DownloadsViewModel dvm)
            {
                DownloadsViewModel = dvm;
                dvm.SukiDialogManager = DialogManager;
            }
        }

        ShowAboutCommand = new RelayCommand(ShowAbout);

        NavigationItems = new ObservableCollection<NavigationItem>(_featureItems);

        SelectedNavigationItem = NavigationItems.FirstOrDefault();

        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => UpdateNavigationTitles());
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    public ISukiToastManager ToastManager { get; }

    public ISukiDialogManager DialogManager { get; }

    public IRelayCommand ShowAboutCommand { get; }

    public DownloadsViewModel? DownloadsViewModel { get; }

    public NavigationItem? SelectedNavigationItem
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                if (value != null)
                {
                    CurrentPage = value.ViewModel;
                }
            }
        }
    }

    public ViewModelBase? CurrentPage
    {
        get;
        set => SetProperty(ref field, value);
    }

    private void UpdateNavigationTitles()
    {
        foreach (var item in _featureItems)
        {
            if (item.ViewModel is IFeatureViewModel feature)
            {
                item.Title = feature.Title;
            }
        }
    }

    private void ShowAbout()
    {
        SelectedNavigationItem = null;
        CurrentPage = _aboutViewModel;
    }
}
