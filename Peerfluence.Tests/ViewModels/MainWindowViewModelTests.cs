using System.IO.Abstractions;
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
        var engineService = new TorrentEngineService(_settingsService, loggerFactory);
        var torrentService = new TorrentService(engineService, Substitute.For<IAppMessenger>());
        var selectionService = new TorrentSelectionService(Substitute.For<IAppMessenger>());
        var topLevelService = Substitute.For<ITopLevelService>();
        var localizationService = new LocalizationService();
        var themeService = new ThemeService();

        _detailsVm = new DetailsViewModel(selectionService, torrentService, localizationService, _notificationService, topLevelService, _settingsService);
        var updateLogger = Substitute.For<Microsoft.Extensions.Logging.ILogger<UpdateService>>();
        _updateService = new UpdateService(updateLogger, _settingsService);
        _settingsVm = new SettingsViewModel(_settingsService, themeService, localizationService, topLevelService, engineService, _updateService, Substitute.For<IWindowsAssociationService>());

        // Create an uninitialized DownloadsViewModel to avoid DispatcherTimer in its constructor
#pragma warning disable SYSLIB0050
        var downloadsVm = (DownloadsViewModel)FormatterServices.GetUninitializedObject(typeof(DownloadsViewModel));
#pragma warning restore SYSLIB0050

        var aboutVm = new AboutViewModel(NullLogger<AboutViewModel>.Instance);

        var features = new IFeatureViewModel[] { downloadsVm, _settingsVm };
        _sut = new MainWindowViewModel(features, aboutVm, _notificationService, _settingsService, _updateService);
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
            updateService);

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
            updateService);

        await sut.CheckForUpdatesOnStartupAsync();

        await updateService.DidNotReceive().CheckForUpdatesAsync();
    }
}
