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
using Peerfluence.Core.Config;
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
    private readonly IInterfaceModeService _interfaceModeService;
    private readonly IDialogService _dialogService;
    private bool _startupUpdateCheckStarted;
    private bool _disposed;

    public MainWindowViewModel(
        IEnumerable<IFeatureViewModel> features,
        AboutViewModel aboutViewModel,
        INotificationService notificationService,
        IAppSettingsService settingsService,
        IUpdateService updateService,
        IInterfaceModeService interfaceModeService,
        IDialogService dialogService,
        DownloadsViewModel downloadsViewModel,
        SettingsViewModel settingsViewModel,
        ISukiToastManager toastManager,
        ISukiDialogManager dialogManager)
    {
        _settingsService = settingsService;
        _updateService = updateService;
        _notificationService = notificationService;
        _aboutViewModel = aboutViewModel;
        _interfaceModeService = interfaceModeService;
        _dialogService = dialogService;

        // Held so the window can bind its toast and dialog hosts to them. Created by the container
        // rather than here, so the services that raise toasts and show prompts are given the same
        // two managers as constructor arguments instead of being reached into and configured.
        ToastManager = toastManager;
        DialogManager = dialogManager;

        // Asked for by name rather than picked out of the feature list by type. They are the same
        // singletons either way, and this says which two screens are special instead of discovering
        // it while building the navigation.
        DownloadsViewModel = downloadsViewModel;
        SettingsPage = settingsViewModel;

        ShowAboutCommand = new RelayCommand(ShowAbout);
        ShowSimpleSettingsCommand = new RelayCommand(() => IsSimpleSettingsOpen = true);
        CloseSimpleSettingsCommand = new RelayCommand(() => IsSimpleSettingsOpen = false);
        ChooseSimpleModeCommand = new AsyncRelayCommand(() => ChooseModeAsync(InterfaceMode.Simple));
        ChooseAdvancedModeCommand = new AsyncRelayCommand(() => ChooseModeAsync(InterfaceMode.Advanced));
        SwitchToAdvancedModeCommand = new AsyncRelayCommand(() => ChooseModeAsync(InterfaceMode.Advanced));

        NavigationItems = new ObservableCollection<NavigationItem>(BuildNavigation(features));

        SelectedNavigationItem = NavigationItems.FirstOrDefault();

        IsSimpleMode = _interfaceModeService.IsSimple;
        IsWelcomeVisible = !_interfaceModeService.HasChosen;

        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => UpdateNavigationTitles());
        WeakReferenceMessenger.Default.Register<InterfaceModeChangedMessage>(this, (_, msg) =>
        {
            IsSimpleMode = msg.Mode == InterfaceMode.Simple;
        });

        // Sent by the Find torrents screen when the problem it is reporting lives in the settings.
        // The settings view model selects the Search tab off the same message; this only has to get
        // the user onto the page.
        WeakReferenceMessenger.Default.Register<ShowSearchSettingsMessage>(this, (_, _) => ShowSettings());
    }

    private void ShowSettings()
    {
        var settings = NavigationItems.FirstOrDefault(item => ReferenceEquals(item.ViewModel, SettingsPage));
        if (settings != null)
        {
            SelectedNavigationItem = settings;
        }
    }

    /// <summary>
    /// Whether the stripped-back interface is showing. Simple mode drops the side menu with it:
    /// there is one screen to be on, so navigation between two of them is noise.
    /// </summary>
    public bool IsSimpleMode
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Whether the first-run welcome is up. Shown until the question has been answered once, and
    /// never again after that.
    /// </summary>
    public bool IsWelcomeVisible
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Simple mode has two screens rather than a menu: the downloads, and settings behind one link.
    /// </summary>
    public bool IsSimpleSettingsOpen
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>
    /// The settings page, so simple mode can show it without the side menu that normally reaches it.
    /// </summary>
    /// <summary>
    /// The settings screen, shown in simple mode where there is no navigation to reach it by.
    /// </summary>
    /// <remarks>
    /// Get only. It is assigned once, from a constructor argument, and a bound property with a
    /// setter that never announces a change is a screen that shows whatever it saw first - which is
    /// what the rule about this asks, and the honest answer here is that it cannot change at all.
    /// </remarks>
    public ViewModelBase? SettingsPage { get; }

    public IRelayCommand ShowSimpleSettingsCommand { get; }

    public IRelayCommand CloseSimpleSettingsCommand { get; }

    public IAsyncRelayCommand ChooseSimpleModeCommand { get; }

    public IAsyncRelayCommand ChooseAdvancedModeCommand { get; }

    public IAsyncRelayCommand SwitchToAdvancedModeCommand { get; }

    private async Task ChooseModeAsync(InterfaceMode mode)
    {
        // Switch first, persist after: the write is a file, and waiting on it before redrawing is
        // what made the change feel like a freeze.
        IsSimpleMode = mode == InterfaceMode.Simple;
        IsWelcomeVisible = false;

        // Leaving settings open across a switch would drop the user somewhere they did not ask to
        // be - the advanced shell has its own way to settings.
        IsSimpleSettingsOpen = false;

        await _interfaceModeService.SetAsync(mode);
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

    /// <summary>
    /// The navigation, in the order the features asked to appear in.
    /// </summary>
    /// <remarks>
    /// A function of its argument and nothing else, so the ordering and the fallback icon can be
    /// checked without building a window's worth of view model. It stays on the way in rather than
    /// moving to an initialise step, because turning a constructor argument into this object's own
    /// state is the one thing a constructor is unarguably for.
    /// </remarks>
    internal static IReadOnlyList<NavigationItem> BuildNavigation(IEnumerable<IFeatureViewModel> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        var items = new List<NavigationItem>();

        foreach (var feature in features.OrderBy(f => f.Order))
        {
            // Named rather than cast. A feature that is not a view model is a registration mistake,
            // and an InvalidCastException at startup names neither the feature nor what was expected
            // of it.
            if (feature is not ViewModelBase viewModel)
            {
                throw new InvalidOperationException(
                    $"{feature.GetType().Name} is registered as a navigation feature but does not "
                        + $"derive from {nameof(ViewModelBase)}, so there is nothing to show for it.");
            }

            // An icon nobody recognises is worth a placeholder rather than a crash: the name is a
            // string in a view model, and getting it wrong should cost a wrong picture.
            var icon = Enum.TryParse<MaterialIconKind>(feature.IconKind, out var parsed)
                ? parsed
                : MaterialIconKind.CircleOutline;

            items.Add(new NavigationItem(feature.Title, icon, viewModel));
        }

        return items;
    }

    private void UpdateNavigationTitles()
    {
        foreach (var item in NavigationItems)
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

    private Task<bool> PromptForStartupUpdateAsync()
    {
        var version = _updateService.AvailableVersion;
        var title = string.IsNullOrWhiteSpace(version)
            ? Resources.UpdatePrompt_Title_Generic
            : string.Format(Resources.UpdatePrompt_Title, version);

        return _dialogService.ConfirmAsync(new ConfirmPrompt(
            title,
            Resources.UpdatePrompt_Message,
            Resources.UpdatePrompt_Install,
            Resources.Common_Later,
            PromptSeverity.Information));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        WeakReferenceMessenger.Default.UnregisterAll(this);
        foreach (var disposable in NavigationItems.Select(item => item.ViewModel).OfType<IDisposable>())
        {
            disposable.Dispose();
        }
    }
}
