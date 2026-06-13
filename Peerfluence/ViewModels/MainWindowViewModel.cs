using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Material.Icons;
using Peerfluence.Core.Messaging;
using Peerfluence.Properties;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace Peerfluence.ViewModels;

[SingletonService]
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IAppSettingsService _settingsService;
    private readonly IUpdateService _updateService;
    private readonly INotificationService _notificationService;
    private readonly AboutViewModel _aboutViewModel;
    private readonly List<NavigationItem> _featureItems = new();
    private bool _startupUpdateCheckStarted;
    private bool _disposed;

    public MainWindowViewModel(
        IEnumerable<IFeatureViewModel> features,
        AboutViewModel aboutViewModel,
        INotificationService notificationService,
        IAppSettingsService settingsService,
        IUpdateService updateService)
    {
        _settingsService = settingsService;
        _updateService = updateService;
        _notificationService = notificationService;
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

    public async Task CheckForUpdatesOnStartupAsync()
    {
        if (_startupUpdateCheckStarted ||
            !_settingsService.Current.Update.CheckForUpdatesOnStartup ||
            !_updateService.CanCheckForUpdates)
        {
            return;
        }

        _startupUpdateCheckStarted = true;

        try
        {
            var hasUpdate = await _updateService.CheckForUpdatesAsync();
            if (!hasUpdate)
            {
                return;
            }

            var installUpdate = await PromptForStartupUpdateAsync();
            if (!installUpdate)
            {
                return;
            }

            _notificationService.Publish(
                new NotificationItem(
                    Resources.Settings_Updates,
                    Resources.Status_DownloadingUpdate,
                    NotificationType.Info,
                    MaterialIconKind.Update.ToString()));

            var downloaded = await _updateService.DownloadUpdateAsync();
            if (downloaded)
            {
                _updateService.ApplyUpdateAndRestart();
                return;
            }

            _notificationService.Publish(
                new NotificationItem(
                    Resources.Settings_Updates,
                    Resources.Status_UpdateCheckFailed,
                    NotificationType.Error,
                    MaterialIconKind.AlertCircleOutline.ToString()),
                TimeSpan.FromSeconds(10));
        }
        catch
        {
            // Startup update checks should never interrupt launching the app.
        }
    }

    private async Task<bool> PromptForStartupUpdateAsync()
    {
        var version = _updateService.AvailableVersion;
        var title = string.IsNullOrWhiteSpace(version)
            ? Resources.UpdatePrompt_Title_Generic
            : string.Format(Resources.UpdatePrompt_Title, version);

        var content = new TextBlock
        {
            Text = Resources.UpdatePrompt_Message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420
        };

        var result = new TaskCompletionSource<bool>();
        await DialogManager
            .CreateDialog()
            .OfType(Avalonia.Controls.Notifications.NotificationType.Information)
            .WithTitle(title)
            .WithContent(content)
            .Dismiss().ByClickingBackground()
            .OnDismissed(_ => result.TrySetResult(false))
            .WithActionButton(Resources.Common_Later, _ => result.TrySetResult(false), true)
            .WithActionButton(Resources.UpdatePrompt_Install, _ => result.TrySetResult(true), true, "Flat")
            .TryShowAsync();

        return result.Task.IsCompletedSuccessfully && result.Task.Result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        WeakReferenceMessenger.Default.UnregisterAll(this);
        DownloadsViewModel?.Dispose();
    }
}
