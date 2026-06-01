using System.Collections.ObjectModel;
using System.IO.Abstractions;
using System.Runtime.Serialization;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Peerfluence.Core;
using Peerfluence.ViewModels;

namespace Peerfluence.HeadlessTests;

internal static class TestHelpers
{
    public static DownloadsViewModel CreateDownloadsViewModel(
        ITorrentService? torrentService = null,
        ITorrentSelectionService? selectionService = null,
        ILocalizationService? localizationService = null,
        ITopLevelService? topLevelService = null,
        IDialogService? dialogService = null,
        IAppSettingsService? settingsService = null,
        DetailsViewModel? detailsViewModel = null)
    {
        torrentService ??= Substitute.For<ITorrentService>();
        selectionService ??= new TorrentSelectionService(Substitute.For<IAppMessenger>());
        localizationService ??= new LocalizationService();
        topLevelService ??= Substitute.For<ITopLevelService>();
        dialogService ??= Substitute.For<IDialogService>();
        settingsService ??= new AppSettingsService(new AppPaths(), Substitute.For<IAppSettingsStore>(), new FileSystem());

        // Workaround: DownloadsViewModel constructor starts background tasks
#pragma warning disable SYSLIB0050
        var vm = (DownloadsViewModel)FormatterServices.GetUninitializedObject(typeof(DownloadsViewModel));
#pragma warning restore SYSLIB0050

        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var fields = typeof(DownloadsViewModel).GetFields(flags);

        fields.First(f => f.Name == "_torrentService").SetValue(vm, torrentService);
        fields.First(f => f.Name == "_selectionService").SetValue(vm, selectionService);
        fields.First(f => f.Name == "_localizationService").SetValue(vm, localizationService);
        fields.First(f => f.Name == "_topLevelService").SetValue(vm, topLevelService);
        fields.First(f => f.Name == "_dialogService").SetValue(vm, dialogService);
        fields.First(f => f.Name == "_settingsService").SetValue(vm, settingsService);

        if (detailsViewModel != null)
        {
            fields.First(f => f.Name == "<SelectedTorrentDetailViewModel>k__BackingField").SetValue(vm, detailsViewModel);
        }

        fields.First(f => f.Name == "<Torrents>k__BackingField").SetValue(vm, new ObservableCollection<TorrentListItemViewModel>());

        // Initialize commands
        fields.First(f => f.Name == "<AddTorrentCommand>k__BackingField").SetValue(vm, new AsyncRelayCommand(() => Task.CompletedTask));
        fields.First(f => f.Name == "<AddMagnetCommand>k__BackingField").SetValue(vm, new AsyncRelayCommand(() => Task.CompletedTask));
        fields.First(f => f.Name == "<ClearStatusCommand>k__BackingField").SetValue(vm, new RelayCommand(() => vm.StatusMessage = string.Empty));
        fields.First(f => f.Name == "<CreateTorrentCommand>k__BackingField").SetValue(vm, new AsyncRelayCommand(() => Task.CompletedTask));
        fields.First(f => f.Name == "<StartSelectedCommand>k__BackingField").SetValue(vm, new AsyncRelayCommand(() => Task.CompletedTask));
        fields.First(f => f.Name == "<StopSelectedCommand>k__BackingField").SetValue(vm, new AsyncRelayCommand(() => Task.CompletedTask));
        fields.First(f => f.Name == "<RemoveSelectedCommand>k__BackingField").SetValue(vm, new AsyncRelayCommand(() => Task.CompletedTask));
        fields.First(f => f.Name == "<OpenFolderCommand>k__BackingField").SetValue(vm, new RelayCommand(() => { }));
        fields.First(f => f.Name == "<CopyHashCommand>k__BackingField").SetValue(vm, new RelayCommand(() => { }));
        fields.First(f => f.Name == "<CopyMagnetCommand>k__BackingField").SetValue(vm, new RelayCommand(() => { }));
        fields.First(f => f.Name == "<ForceRecheckCommand>k__BackingField").SetValue(vm, new AsyncRelayCommand(() => Task.CompletedTask));

        return vm;
    }

    public static DetailsViewModel CreateDetailsViewModel()
    {
        var selectionService = new TorrentSelectionService(Substitute.For<IAppMessenger>());
        var torrentService = Substitute.For<ITorrentService>();
        var localizationService = new LocalizationService();
        var notificationService = Substitute.For<INotificationService>();
        var topLevelService = Substitute.For<ITopLevelService>();
        var store = Substitute.For<IAppSettingsStore>();
        var settingsService = new AppSettingsService(new AppPaths(), store, new FileSystem());

        var vm = new DetailsViewModel(
            selectionService,
            torrentService,
            localizationService,
            notificationService,
            topLevelService,
            settingsService);

        // Replace Dispatcher call with synchronous execution for tests
        vm.UIDispatcher = action => action();

        return vm;
    }

    public static SettingsViewModel CreateSettingsViewModel()
    {
        var store = Substitute.For<IAppSettingsStore>();
        var settingsService = new AppSettingsService(new AppPaths(), store, new FileSystem());
        var themeService = Substitute.For<IThemeService>();
        var localizationService = new LocalizationService();
        var topLevelService = Substitute.For<ITopLevelService>();
        var engineService = Substitute.For<ITorrentEngineService>();
        var updateService = Substitute.For<IUpdateService>();
        var windowsAssociationService = Substitute.For<IWindowsAssociationService>();

        return new SettingsViewModel(
            settingsService,
            themeService,
            localizationService,
            topLevelService,
            engineService,
            updateService,
            windowsAssociationService);
    }

    public static CreateTorrentViewModel CreateCreateTorrentViewModel()
    {
        var topLevelService = Substitute.For<ITopLevelService>();
        var logger = Substitute.For<ILogger<CreateTorrentViewModel>>();
        return new CreateTorrentViewModel(topLevelService, logger);
    }

    public static MainWindowViewModel CreateMainWindowViewModel(
        DownloadsViewModel? downloadsVm = null,
        SettingsViewModel? settingsVm = null,
        AboutViewModel? aboutVm = null)
    {
        downloadsVm ??= CreateDownloadsViewModel();
        settingsVm ??= CreateSettingsViewModel();
        aboutVm ??= new AboutViewModel(Substitute.For<ILogger<AboutViewModel>>());
        var notificationService = Substitute.For<INotificationService>();

        var features = new List<IFeatureViewModel>();
        if (downloadsVm is IFeatureViewModel df) features.Add(df);
        if (settingsVm is IFeatureViewModel sf) features.Add(sf);

        return new MainWindowViewModel(
            features,
            aboutVm,
            notificationService);
    }
}
