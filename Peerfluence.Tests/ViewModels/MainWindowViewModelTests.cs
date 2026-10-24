using Peerfluence.Theme.Controls;
using System.IO.Abstractions;
using System.Reflection;
using System.Runtime.Serialization;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Peerfluence.ViewModels;

namespace Peerfluence.Tests.ViewModels;

[Collection("Messenger")]
public class MainWindowViewModelTests
{
    private readonly DetailsViewModel _detailsVm;
    private readonly DownloadsViewModel _downloadsVm;
    private readonly SettingsViewModel _settingsVm;
    private readonly INotificationService _notificationService;
    private readonly IAppSettingsService _settingsService;
    private readonly IUpdateService _updateService;
    private readonly MainWindowViewModel _sut;

    public MainWindowViewModelTests()
    {
        WeakReferenceMessenger.Default.Reset();

        _notificationService = Substitute.For<INotificationService>();

        var store = Substitute.For<IAppSettingsStore>();
        var paths = new AppPaths();
        var fileSystem = new FileSystem();
        _settingsService = new AppSettingsService(paths, store, fileSystem);
        var loggerFactory = Substitute.For<Microsoft.Extensions.Logging.ILoggerFactory>();
        var engineService = new TorrentEngineService(_settingsService, loggerFactory, TimeProvider.System);
        var torrentService = new TorrentService(engineService, Substitute.For<IAppMessenger>(), new HttpClient(), SeedingDefaults.Off);
        var selectionService = new TorrentSelectionService(Substitute.For<IAppMessenger>());
        var topLevelService = Substitute.For<ITopLevelService>();
        var localizationService = new LocalizationService();
        var themeService = new ThemeService();

        _detailsVm = new DetailsViewModel(selectionService, torrentService, localizationService, _notificationService, topLevelService, _settingsService, Substitute.For<IDialogService>(), Substitute.For<IMediaStreamingService>());
        var updateLogger = Substitute.For<Microsoft.Extensions.Logging.ILogger<UpdateService>>();
        _updateService = new UpdateService(updateLogger, _settingsService);
        _settingsVm = new SettingsViewModel(_settingsService, themeService, localizationService, topLevelService, engineService, _updateService, Substitute.For<IWindowsAssociationService>(), Substitute.For<IInterfaceModeService>(), Substitute.For<ITorrentSearchService>(), Substitute.For<ITorrentCategoryService>());

        // Create an uninitialized DownloadsViewModel to avoid DispatcherTimer in its constructor
#pragma warning disable SYSLIB0050
        _downloadsVm = (DownloadsViewModel)FormatterServices.GetUninitializedObject(typeof(DownloadsViewModel));
        var downloadsVm = _downloadsVm;
#pragma warning restore SYSLIB0050

        // The real constructor always assigns this, and MainWindowViewModel reaches through it to
        // hand the details pane the dialog manager, so an uninitialized object needs it set.
        var downloadsFields = typeof(DownloadsViewModel).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        downloadsFields.First(f => f.Name == "<SelectedTorrentDetailViewModel>k__BackingField").SetValue(downloadsVm, _detailsVm);

        // MainWindowViewModel disposes the feature view models it was handed, and DownloadsViewModel
        // reaches for these when it does. A real one gets them from its constructor.
        downloadsFields.First(f => f.Name == "<Torrents>k__BackingField")
            .SetValue(downloadsVm, new System.Collections.ObjectModel.ObservableCollection<TorrentListItemViewModel>());
        downloadsFields.First(f => f.Name == "_loopCts").SetValue(downloadsVm, new CancellationTokenSource());
        downloadsFields.First(f => f.Name == "_alertChannel").SetValue(
            downloadsVm,
            System.Threading.Channels.Channel.CreateBounded<TorrentAlertEventArgs>(
                new System.Threading.Channels.BoundedChannelOptions(1)
                {
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
                }));

        var aboutVm = new AboutViewModel(NullLogger<AboutViewModel>.Instance);

        var features = new IFeatureViewModel[] { downloadsVm, _settingsVm };
        _sut = new MainWindowViewModel(features, aboutVm, _notificationService, _settingsService, _updateService, Substitute.For<IInterfaceModeService>(),
            Substitute.For<IDialogService>(),
            downloadsVm,
            _settingsVm,
            Substitute.For<IToastManager>(),
            Substitute.For<IDialogManager>());
    }

    [Fact]
    public void NavigationItems_ContainsTwoItems()
    {
        Assert.Equal(2, _sut.NavigationItems.Count);
    }

    [Fact]
    public void SelectedNavigationItem_DefaultsToFirst()
    {
        Assert.Same(_sut.NavigationItems[0], _sut.SelectedNavigationItem);
    }

    [Fact]
    public void ChangingSelectedNavigation_UpdatesCurrentPage()
    {
        _sut.SelectedNavigationItem = _sut.NavigationItems[1];
        Assert.Same(_settingsVm, _sut.CurrentPage);
    }

    [Fact]
    public void SelectedNavigationItem_RaisesPropertyChanged()
    {
        var changedProperties = new List<string>();
        _sut.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        _sut.SelectedNavigationItem = _sut.NavigationItems[1];

        Assert.Contains(nameof(_sut.SelectedNavigationItem), changedProperties);
        Assert.Contains(nameof(_sut.CurrentPage), changedProperties);
    }

    [Fact]
    public void ToastManager_IsNotNull()
    {
        Assert.NotNull(_sut.ToastManager);
    }

    [Fact]
    public void DialogManager_IsNotNull()
    {
        Assert.NotNull(_sut.DialogManager);
    }

    [Fact]
    public async Task CheckForUpdatesOnStartupAsync_DoesNothing_WhenSettingDisabled()
    {
        var settings = Substitute.For<IAppSettingsService>();
        settings.Current.Returns(new AppSettings { Update = { CheckForUpdatesOnStartup = false } });
        var updateService = Substitute.For<IUpdateService>();
        updateService.CanCheckForUpdates.Returns(true);

        var sut = new MainWindowViewModel(
            Array.Empty<IFeatureViewModel>(),
            new AboutViewModel(NullLogger<AboutViewModel>.Instance),
            _notificationService,
            settings,
            updateService,
            Substitute.For<IInterfaceModeService>(),
            Substitute.For<IDialogService>(),
            _downloadsVm,
            _settingsVm,
            Substitute.For<IToastManager>(),
            Substitute.For<IDialogManager>());

        await sut.CheckForUpdatesOnStartupAsync();

        await updateService.DidNotReceive().CheckForUpdatesAsync();
    }

    [Fact]
    public async Task CheckForUpdatesOnStartupAsync_DoesNothing_WhenUpdatesCannotBeChecked()
    {
        var settings = Substitute.For<IAppSettingsService>();
        settings.Current.Returns(new AppSettings { Update = { CheckForUpdatesOnStartup = true } });
        var updateService = Substitute.For<IUpdateService>();
        updateService.CanCheckForUpdates.Returns(false);

        var sut = new MainWindowViewModel(
            Array.Empty<IFeatureViewModel>(),
            new AboutViewModel(NullLogger<AboutViewModel>.Instance),
            _notificationService,
            settings,
            updateService,
            Substitute.For<IInterfaceModeService>(),
            Substitute.For<IDialogService>(),
            _downloadsVm,
            _settingsVm,
            Substitute.For<IToastManager>(),
            Substitute.For<IDialogManager>());

        await sut.CheckForUpdatesOnStartupAsync();

        await updateService.DidNotReceive().CheckForUpdatesAsync();
    }

    [Fact]
    public void DisposingTheWindow_CanHappenTwice()
    {
        // The window closes and then the host disposes the container. Both call this.
        _sut.Dispose();
        _sut.Dispose();
    }

    private (MainWindowViewModel Window, StatisticsViewModel Statistics, ITorrentService Engine) WithStatistics()
    {
        var engine = Substitute.For<ITorrentService>();
        engine.GetStats().Returns(new PeerSharp.Core.EngineStats());
        var statistics = new StatisticsViewModel(engine);

        var window = new MainWindowViewModel(
            new IFeatureViewModel[] { _downloadsVm, statistics, _settingsVm },
            new AboutViewModel(NullLogger<AboutViewModel>.Instance),
            _notificationService,
            _settingsService,
            _updateService,
            Substitute.For<IInterfaceModeService>(),
            Substitute.For<IDialogService>(),
            _downloadsVm,
            _settingsVm,
            Substitute.For<IToastManager>(),
            Substitute.For<IDialogManager>());

        return (window, statistics, engine);
    }

    private static NavigationItem ItemFor(MainWindowViewModel window, ViewModelBase page) =>
        window.NavigationItems.Single(item => ReferenceEquals(item.ViewModel, page));

    [Fact]
    public void AStatisticsPageNobodyHasOpened_IsNotOnScreen()
    {
        var (_, statistics, engine) = WithStatistics();

        Assert.False(statistics.IsOnScreen);
        engine.DidNotReceive().GetStats();
    }

    [Fact]
    public void SelectingStatistics_PutsItOnScreen_AndLeavingTakesItOff()
    {
        var (window, statistics, engine) = WithStatistics();

        window.SelectedNavigationItem = ItemFor(window, statistics);

        Assert.True(statistics.IsOnScreen);
        engine.Received(1).GetStats();

        window.SelectedNavigationItem = ItemFor(window, _settingsVm);

        Assert.False(statistics.IsOnScreen);
    }

    [Fact]
    public void ShowingAbout_TakesTheCurrentPageOffScreen()
    {
        var (window, statistics, _) = WithStatistics();
        window.SelectedNavigationItem = ItemFor(window, statistics);

        window.ShowAboutCommand.Execute(null);

        Assert.False(statistics.IsOnScreen);
    }

    [Fact]
    public async Task SimpleMode_HidesTheAdvancedPage_AndSwitchingBackShowsItAgain()
    {
        // Simple mode leaves the current page as it was and draws its own screen over it.
        var (window, statistics, _) = WithStatistics();
        window.SelectedNavigationItem = ItemFor(window, statistics);

        await window.ChooseSimpleModeCommand.ExecuteAsync(null);
        Assert.False(statistics.IsOnScreen);

        await window.ChooseAdvancedModeCommand.ExecuteAsync(null);
        Assert.True(statistics.IsOnScreen);
    }

    [Fact]
    public async Task SimpleDownloadsPage_IsTheDownloadsOnlyInSimpleMode()
    {
        // Null outside simple mode, so the simple screen's view is never built for it.
        Assert.Null(_sut.SimpleDownloadsPage);

        var changed = new List<string>();
        _sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
        await _sut.ChooseSimpleModeCommand.ExecuteAsync(null);

        Assert.Same(_downloadsVm, _sut.SimpleDownloadsPage);
        Assert.Contains(nameof(_sut.SimpleDownloadsPage), changed);

        await _sut.ChooseAdvancedModeCommand.ExecuteAsync(null);
        Assert.Null(_sut.SimpleDownloadsPage);
    }
}
