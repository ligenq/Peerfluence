using System.IO.Abstractions;
using System.Runtime.Serialization;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core;
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
    private readonly MainWindowViewModel _sut;

    public MainWindowViewModelTests()
    {
        WeakReferenceMessenger.Default.Reset();

        _notificationService = Substitute.For<INotificationService>();

        var store = Substitute.For<IAppSettingsStore>();
        var paths = new AppPaths();
        var fileSystem = new FileSystem();
        var settingsService = new AppSettingsService(paths, store, fileSystem);
        var loggerFactory = Substitute.For<Microsoft.Extensions.Logging.ILoggerFactory>();
        var engineService = new TorrentEngineService(settingsService, loggerFactory);
        var torrentService = new TorrentService(engineService, Substitute.For<IAppMessenger>());
        var selectionService = new TorrentSelectionService(Substitute.For<IAppMessenger>());
        var topLevelService = Substitute.For<ITopLevelService>();
        var localizationService = new LocalizationService();
        var themeService = new ThemeService();

        _detailsVm = new DetailsViewModel(selectionService, torrentService, localizationService, _notificationService, topLevelService, settingsService);
        var updateLogger = Substitute.For<Microsoft.Extensions.Logging.ILogger<UpdateService>>();
        var updateService = new UpdateService(updateLogger, settingsService);
        _settingsVm = new SettingsViewModel(settingsService, themeService, localizationService, topLevelService, engineService, updateService, Substitute.For<IWindowsAssociationService>());

        // Create an uninitialized DownloadsViewModel to avoid DispatcherTimer in its constructor
#pragma warning disable SYSLIB0050
        var downloadsVm = (DownloadsViewModel)FormatterServices.GetUninitializedObject(typeof(DownloadsViewModel));
#pragma warning restore SYSLIB0050

        var aboutVm = new AboutViewModel(NullLogger<AboutViewModel>.Instance);

        var features = new IFeatureViewModel[] { downloadsVm, _settingsVm };
        _sut = new MainWindowViewModel(features, aboutVm, _notificationService);
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
}
