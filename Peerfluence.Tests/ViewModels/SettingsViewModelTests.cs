using System.IO.Abstractions;
using System.Globalization;
using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Core.Config;
using Peerfluence.Core.Messaging;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Peerfluence.ViewModels;

namespace Peerfluence.Tests.ViewModels;

/// <summary>
/// In the same collection as everything else that touches process-wide state, because this class
/// changes the application's language: applying a culture is global, and a class reading localized
/// display names in parallel with this one reads whichever language happened to be in force.
/// </summary>
[Collection("Messenger")]
public class SettingsViewModelTests
{
    private readonly IAppSettingsService _settingsService;
    private readonly ThemeService _themeService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly ITopLevelService _topLevelService = Substitute.For<ITopLevelService>();
    private readonly ITorrentEngineService _engineService;
    private readonly IWindowsAssociationService _windowsAssociationService = Substitute.For<IWindowsAssociationService>();
    private readonly SettingsViewModel _sut;

    public SettingsViewModelTests()
    {
        var store = Substitute.For<IAppSettingsStore>();
        var paths = new AppPaths();
        _settingsService = new AppSettingsService(paths, store, new FileSystem());

        var loggerFactory = Substitute.For<Microsoft.Extensions.Logging.ILoggerFactory>();
        _engineService = new TorrentEngineService(_settingsService, loggerFactory);

        var updateLogger = Substitute.For<Microsoft.Extensions.Logging.ILogger<UpdateService>>();
        var updateService = new UpdateService(updateLogger, _settingsService);

        // Answers as the real service does. A bare substitute returns a completed task with a null
        // result, which is not something this service is allowed to do.
        var searchService = Substitute.For<ITorrentSearchService>();
        searchService.TestAsync(Arg.Any<CancellationToken>()).Returns(TorrentSearchResponse.Succeeded([]));

        _sut = new SettingsViewModel(
            _settingsService,
            _themeService,
            _localizationService,
            _topLevelService,
            _engineService,
            updateService,
            _windowsAssociationService,
            Substitute.For<IInterfaceModeService>(),
            searchService,
            Substitute.For<ITorrentCategoryService>());
    }

    [Fact]
    public void InitialState_LoadsFromSettings()
    {
        Assert.Equal(_settingsService.Current.Network.EnableDht, _sut.EnableDht);
        Assert.Equal(_settingsService.Current.Network.EnableNatPmp, _sut.EnableNatPmp);
        Assert.Equal(_settingsService.Current.Network.EnableUpnp, _sut.EnableUpnp);
        Assert.Equal(_settingsService.Current.Network.UseAutomaticListeningPort, _sut.UseAutomaticListeningPort);
        Assert.Equal(_settingsService.Current.Network.ListeningPort, _sut.ListeningPort);
        Assert.Equal(_settingsService.Current.Storage.EnableSessionPersistence, _sut.EnableSessionPersistence);
        Assert.Equal(_settingsService.Current.ShowRemoveTorrentOptions, _sut.ShowRemoveTorrentOptions);
        Assert.Equal(_settingsService.Current.AssociateTorrentFiles, _sut.AssociateTorrentFiles);
        Assert.Equal(_settingsService.Current.AssociateMagnetLinks, _sut.AssociateMagnetLinks);
        Assert.Equal(_settingsService.Current.CompletionAction.Enabled, _sut.CompletionActionEnabled);
        Assert.Equal(_settingsService.Current.CompletionAction.WorkingDirectoryTemplate, _sut.CompletionActionWorkingDirectoryTemplate);
    }

    [Fact]
    public void ThemeVariants_ContainsExpectedValues()
    {
        Assert.Contains("System", _sut.ThemeVariants);
        Assert.Contains("Light", _sut.ThemeVariants);
        Assert.Contains("Dark", _sut.ThemeVariants);
    }

    [Fact]
    public void ColorThemes_ContainsExpectedValues()
    {
        Assert.Contains("Indigo", _sut.ColorThemes);
        Assert.Contains("Cobalt", _sut.ColorThemes);
        Assert.Contains("Rose", _sut.ColorThemes);
    }

    [Fact]
    public void BackgroundStyles_ContainsExpectedValues()
    {
        Assert.Contains("GradientSoft", _sut.BackgroundStyles);
        Assert.Contains("Flat", _sut.BackgroundStyles);
    }

    [Fact]
    public void EncryptionModes_ContainsExpectedValues()
    {
        Assert.Contains("Allow", _sut.EncryptionModes);
        Assert.Contains("Require", _sut.EncryptionModes);
        Assert.Contains("Refuse", _sut.EncryptionModes);
    }

    [Fact]
    public void ProxyTypes_ContainsExpectedValues()
    {
        Assert.Contains("None", _sut.ProxyTypes);
        Assert.Contains("Socks5", _sut.ProxyTypes);
        Assert.Contains("Http", _sut.ProxyTypes);
    }

    [Fact]
    public void Languages_ContainsSupportedCultures()
    {
        Assert.Contains("en-US", _sut.Languages);
        Assert.Contains("sv-SE", _sut.Languages);
        Assert.Contains("es-ES", _sut.Languages);
        Assert.Contains("de-DE", _sut.Languages);
        Assert.Contains("fr-FR", _sut.Languages);
        Assert.Contains("pl-PL", _sut.Languages);
        Assert.Contains("it-IT", _sut.Languages);
        Assert.Contains("pt-PT", _sut.Languages);
        Assert.Contains("ru-RU", _sut.Languages);
        Assert.Contains("uk-UA", _sut.Languages);
    }

    [Fact]
    public void LanguageOptions_DisplayNativeNamesWithCultureCodes()
    {
        Assert.Contains(_sut.LanguageOptions, option => option.Value == "en-US" && option.DisplayName == "English (en-US)");
        Assert.Contains(_sut.LanguageOptions, option => option.Value == "sv-SE" && option.DisplayName == "Svenska (sv-SE)");
        Assert.Contains(_sut.LanguageOptions, option => option.Value == "es-ES" && option.DisplayName == "Español (es-ES)");
        Assert.Contains(_sut.LanguageOptions, option => option.Value == "de-DE" && option.DisplayName == "Deutsch (de-DE)");
        Assert.Contains(_sut.LanguageOptions, option => option.Value == "fr-FR" && option.DisplayName == "Français (fr-FR)");
        Assert.Contains(_sut.LanguageOptions, option => option.Value == "pl-PL" && option.DisplayName == "Polski (pl-PL)");
        Assert.Contains(_sut.LanguageOptions, option => option.Value == "it-IT" && option.DisplayName == "Italiano (it-IT)");
        Assert.Contains(_sut.LanguageOptions, option => option.Value == "pt-PT" && option.DisplayName == "Português (pt-PT)");
        Assert.Contains(_sut.LanguageOptions, option => option.Value == "ru-RU" && option.DisplayName == "Русский (ru-RU)");
        Assert.Contains(_sut.LanguageOptions, option => option.Value == "uk-UA" && option.DisplayName == "Українська (uk-UA)");
    }

    [Fact]
    public void LanguageOptions_AreSortedByCultureCode()
    {
        var values = _sut.LanguageOptions.Select(option => option.Value).ToArray();

        Assert.Equal(values.OrderBy(value => value, StringComparer.Ordinal).ToArray(), values);
    }

    [Fact]
    public void ApplicationVersion_IsAvailable()
    {
        Assert.False(string.IsNullOrWhiteSpace(_sut.ApplicationVersion));
        Assert.Matches(@"^\d+\.\d+\.\d+$", _sut.ApplicationVersion);
    }

    [Theory]
    [InlineData("sv-SE", "Språk")]
    [InlineData("es-ES", "Idioma")]
    [InlineData("de-DE", "Sprache")]
    [InlineData("fr-FR", "Langue")]
    [InlineData("pl-PL", "Język")]
    [InlineData("it-IT", "Lingua")]
    [InlineData("pt-PT", "Idioma")]
    [InlineData("ru-RU", "Язык")]
    [InlineData("uk-UA", "Мова")]
    public void LocalizedResources_AreAvailable(string cultureName, string expected)
    {
        var value = Properties.Resources.ResourceManager.GetString(
            nameof(Properties.Resources.Settings_Language),
            CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expected, value);
    }

    [Fact]
    public void MaxDiskReadSpeedBytesPerSecond_ClampsToZero()
    {
        _sut.MaxDiskReadSpeedBytesPerSecond = -100;
        Assert.Equal(0, _sut.MaxDiskReadSpeedBytesPerSecond);
    }

    [Fact]
    public void MaxDiskWriteSpeedBytesPerSecond_ClampsToZero()
    {
        _sut.MaxDiskWriteSpeedBytesPerSecond = -50;
        Assert.Equal(0, _sut.MaxDiskWriteSpeedBytesPerSecond);
    }

    [Fact]
    public void MaxActiveDownloads_ClampsToZero()
    {
        _sut.MaxActiveDownloads = -1;
        Assert.Equal(0, _sut.MaxActiveDownloads);
    }

    [Fact]
    public void MaxActiveSeeds_ClampsToZero()
    {
        _sut.MaxActiveSeeds = -1;
        Assert.Equal(0, _sut.MaxActiveSeeds);
    }

    [Fact]
    public void ProxyPort_ClampsToValidRange()
    {
        _sut.ProxyPort = -1;
        Assert.Equal(0, _sut.ProxyPort);

        _sut.ProxyPort = 70000;
        Assert.Equal(65535, _sut.ProxyPort);

        _sut.ProxyPort = 8080;
        Assert.Equal(8080, _sut.ProxyPort);
    }

    [Fact]
    public void ListeningPort_ClampsToValidRange()
    {
        _sut.ListeningPort = -1;
        Assert.Equal(1, _sut.ListeningPort);

        _sut.ListeningPort = 70000;
        Assert.Equal(65535, _sut.ListeningPort);

        _sut.ListeningPort = 51413;
        Assert.Equal(51413, _sut.ListeningPort);
    }

    [Fact]
    public void UseAutomaticListeningPort_RaisesFixedPortEnabledChange()
    {
        var changedProperties = new List<string>();
        _sut.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        _sut.UseAutomaticListeningPort = true;

        Assert.False(_sut.IsFixedListeningPortEnabled);
        Assert.Contains(nameof(_sut.UseAutomaticListeningPort), changedProperties);
        Assert.Contains(nameof(_sut.IsFixedListeningPortEnabled), changedProperties);
    }

    [Fact]
    public void PortMappingStatuses_InitiallyEmpty()
    {
        Assert.Empty(_sut.PortMappingStatuses);
    }

    [Fact]
    public void Properties_RaisePropertyChanged()
    {
        var changedProperties = new List<string>();
        _sut.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        _sut.DownloadPath = "/new/path";
        _sut.EnableDht = !_sut.EnableDht;
        _sut.UseAutomaticListeningPort = !_sut.UseAutomaticListeningPort;
        _sut.ShowRemoveTorrentOptions = !_sut.ShowRemoveTorrentOptions;
        _sut.EnableBlocklist = true;
        _sut.SelectedEncryptionMode = "Require";
        _sut.SelectedProxyType = "Socks5";

        Assert.Contains(nameof(_sut.DownloadPath), changedProperties);
        Assert.Contains(nameof(_sut.EnableDht), changedProperties);
        Assert.Contains(nameof(_sut.UseAutomaticListeningPort), changedProperties);
        Assert.Contains(nameof(_sut.ShowRemoveTorrentOptions), changedProperties);
        Assert.Contains(nameof(_sut.EnableBlocklist), changedProperties);
        Assert.Contains(nameof(_sut.SelectedEncryptionMode), changedProperties);
        Assert.Contains(nameof(_sut.SelectedProxyType), changedProperties);
    }

    [Fact]
    public void ResetDefaultsCommand_ResetsStatusMessage()
    {
        _sut.StatusMessage = "Something";
        _sut.ResetDefaultsCommand.Execute(null);

        Assert.Equal(Properties.Resources.Status_SettingsReset, _sut.StatusMessage);
    }

    [Fact]
    public void ResetDefaultsCommand_RestoresFactoryDefaultsInsteadOfCurrentSettings()
    {
        _settingsService.Current.Storage.DownloadPath = "/persisted/path";
        _settingsService.Current.Network.EnableDht = false;
        _settingsService.Current.Network.UseAutomaticListeningPort = true;
        _settingsService.Current.Network.ListeningPort = 51413;
        _settingsService.Current.ShowRemoveTorrentOptions = false;
        _settingsService.Current.Update.UpdateUrl = "https://persisted.example/feed";

        _sut.DownloadPath = "/changed/path";
        _sut.EnableDht = true;
        _sut.UseAutomaticListeningPort = false;
        _sut.ListeningPort = 60000;
        _sut.ShowRemoveTorrentOptions = false;
        _sut.UpdateUrl = "https://changed.example/feed";

        _sut.ResetDefaultsCommand.Execute(null);

        // Stated against the factory default directly. This used to be phrased as "the view model
        // differs from the stored settings", which no longer says anything: a change now reaches
        // the settings object as it is made, so the two agree by design.
        Assert.Equal(_settingsService.CreateDefaultSettings().Storage.DownloadPath, _sut.DownloadPath);
        Assert.NotEqual("/persisted/path", _sut.DownloadPath);
        Assert.Equal(UpdateSettings.DefaultUpdateUrl, _sut.UpdateUrl);
        Assert.True(_sut.EnableDht);
        Assert.False(_sut.UseAutomaticListeningPort);
        Assert.Equal(6881, _sut.ListeningPort);
        Assert.True(_sut.ShowRemoveTorrentOptions);
    }

    [Fact]
    public void Commands_AreNotNull()
    {
        Assert.NotNull(_sut.SaveCommand);
        Assert.NotNull(_sut.ResetDefaultsCommand);
        Assert.NotNull(_sut.BrowseBlocklistCommand);
        Assert.NotNull(_sut.BrowseGeoIpCommand);
        Assert.NotNull(_sut.BrowseCompletionActionProgramCommand);
        Assert.NotNull(_sut.RefreshPortMappingCommand);
    }

    [Fact]
    public async Task SaveCommand_PersistsSettingsAndShowsMessage()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"peerfluence-settings-{Guid.NewGuid():N}");
        _sut.DownloadPath = downloadPath;
        _sut.EnableDht = true;
        _sut.UseAutomaticListeningPort = true;
        _sut.ListeningPort = 51413;
        _sut.ShowRemoveTorrentOptions = false;
        _sut.AssociateTorrentFiles = true;
        _sut.AssociateMagnetLinks = true;
        _sut.MaxActiveDownloads = 10;
        _sut.CompletionActionEnabled = true;
        _sut.CompletionActionProgramPath = "/bin/tool";
        _sut.CompletionActionArgumentsTemplate = "--path {downloadPath}";
        _sut.CompletionActionTimeoutSeconds = 60;

        await _sut.SaveCommand.ExecuteAsync(null);

        Assert.Equal(downloadPath, _settingsService.Current.Storage.DownloadPath);
        Assert.True(_settingsService.Current.Network.EnableDht);
        Assert.True(_settingsService.Current.Network.UseAutomaticListeningPort);
        Assert.Equal(51413, _settingsService.Current.Network.ListeningPort);
        Assert.False(_settingsService.Current.ShowRemoveTorrentOptions);
        Assert.True(_settingsService.Current.AssociateTorrentFiles);
        Assert.True(_settingsService.Current.AssociateMagnetLinks);
        Assert.Equal(10, _settingsService.Current.Queue.MaxActiveDownloads);
        Assert.True(_settingsService.Current.CompletionAction.Enabled);
        Assert.Equal("/bin/tool", _settingsService.Current.CompletionAction.ProgramPath);
        Assert.Equal("--path {downloadPath}", _settingsService.Current.CompletionAction.ArgumentsTemplate);
        Assert.Equal(60, _settingsService.Current.CompletionAction.TimeoutSeconds);
        Assert.Equal(Properties.Resources.Status_SettingsSaved, _sut.StatusMessage);
        _windowsAssociationService.Received(1).ApplyAssociations(true, true);
    }

    [Fact]
    public async Task SaveCommand_WhenLanguageChanges_PreservesOptionSelections()
    {
        try
        {
            _sut.SelectedThemeVariant = "Dark";
            _sut.SelectedColorTheme = "Emerald";
            _sut.SelectedBackgroundStyle = "Flat";
            _sut.SelectedEncryptionMode = "Require";
            _sut.SelectedProxyType = "Socks5";
            _sut.SelectedLanguage = "sv-SE";

            await _sut.SaveCommand.ExecuteAsync(null);

            Assert.Equal("Dark", _sut.SelectedThemeVariant);
            Assert.Equal("Emerald", _sut.SelectedColorTheme);
            Assert.Equal("Flat", _sut.SelectedBackgroundStyle);
            Assert.Equal("Require", _sut.SelectedEncryptionMode);
            Assert.Equal("Socks5", _sut.SelectedProxyType);
            Assert.Equal("sv-SE", _sut.SelectedLanguage);
        }
        finally
        {
            // Settling the debounced save first. Changing the language schedules one, and a save
            // that lands after the line below has put the culture back would leave every later test
            // reading Swedish.
            await _sut.WaitForPendingSaveAsync();
            _localizationService.Apply("en-US");
        }
    }

    [Fact]
    public async Task ChangingASetting_SavesItWithoutBeingAsked()
    {
        var store = Substitute.For<IAppSettingsStore>();
        var settingsService = new AppSettingsService(new AppPaths(), store, new FileSystem());
        var sut = Create(settingsService);

        sut.EnableDht = !sut.EnableDht;
        var saved = await WaitForSaveAsync(store);

        Assert.True(saved, "the change should have been written without a Save button");
        Assert.Equal(sut.EnableDht, settingsService.Current.Network.EnableDht);
    }

    [Fact]
    public async Task ABurstOfChanges_IsWrittenOnce()
    {
        var store = Substitute.For<IAppSettingsStore>();
        var settingsService = new AppSettingsService(new AppPaths(), store, new FileSystem());
        var sut = Create(settingsService);

        // What dragging a slider, or Reset writing every field, looks like.
        sut.MaxActiveDownloads = 3;
        sut.MaxActiveDownloads = 4;
        sut.MaxActiveDownloads = 5;
        await WaitForSaveAsync(store);

        await store.Received(1).SaveAsync(Arg.Any<AppSettings>(), Arg.Any<CancellationToken>());
        Assert.Equal(5, settingsService.Current.Queue.MaxActiveDownloads);
    }

    [Fact]
    public void AChangeIsInForceBeforeItReachesTheDisk()
    {
        // The point of splitting the two: closing the window inside the debounce window used to
        // lose the change, because the view model was still the only thing that knew about it.
        var store = Substitute.For<IAppSettingsStore>();
        var settingsService = new AppSettingsService(new AppPaths(), store, new FileSystem());
        var sut = Create(settingsService);
        var before = settingsService.Current.Network.EnableDht;

        sut.EnableDht = !before;

        // No waiting: the settings object has it already, which is what shutdown writes and what
        // the rest of the application reads.
        Assert.Equal(!before, settingsService.Current.Network.EnableDht);
    }

    [Fact]
    public async Task AChangeMadeAndAbandonedImmediately_IsStillWrittenByTheShutdownSave()
    {
        var store = Substitute.For<IAppSettingsStore>();
        var settingsService = new AppSettingsService(new AppPaths(), store, new FileSystem());
        var sut = Create(settingsService);

        sut.ListeningPort = 51999;

        // Shutting down before the debounce has come round at all.
        var shutdown = new AppSettingsHostedService(settingsService);
        await shutdown.StopAsync(TestContext.Current.CancellationToken);

        await store.Received().SaveAsync(
            Arg.Is<AppSettings>(s => s.Network.ListeningPort == 51999),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AComboBoxReportingNothingSelected_IsNotTakenAsAChoice()
    {
        // What a ComboBox does for a moment when its items are replaced. It is not the user
        // choosing "no theme" - there is no such choice - and taking it as one stored a null theme
        // and handed it to the theme service, which threw on the lookup.
        var before = _sut.SelectedColorTheme;

        _sut.SelectedColorTheme = null!;
        _sut.SelectedThemeVariant = null!;
        _sut.SelectedBackgroundStyle = null!;
        _sut.SelectedEncryptionMode = string.Empty;
        _sut.SelectedProxyType = string.Empty;

        Assert.Equal(before, _sut.SelectedColorTheme);
        Assert.False(string.IsNullOrEmpty(_sut.SelectedThemeVariant));
        Assert.False(string.IsNullOrEmpty(_sut.SelectedBackgroundStyle));
        Assert.False(string.IsNullOrEmpty(_sut.SelectedEncryptionMode));
        Assert.False(string.IsNullOrEmpty(_sut.SelectedProxyType));
    }

    [Fact]
    public async Task ChangingTheLanguage_DoesNotBlankTheOtherChoicesOnTheWay()
    {
        // Changing the language relabels all five choice lists, which is what replaces their items.
        var store = Substitute.For<IAppSettingsStore>();
        var settingsService = new AppSettingsService(new AppPaths(), store, new FileSystem());
        var localizationService = Substitute.For<ILocalizationService>();
        var sut = Create(settingsService, localizationService: localizationService);

        try
        {
            sut.SelectedLanguage = "sv-SE";
            await WaitForSaveAsync(store);
            await sut.WaitForPendingSaveAsync();

            Assert.False(string.IsNullOrEmpty(settingsService.Current.Theme.ColorTheme));
            Assert.False(string.IsNullOrEmpty(settingsService.Current.Theme.ThemeVariant));
            Assert.False(string.IsNullOrEmpty(settingsService.Current.Theme.BackgroundStyle));
        }
        finally
        {
            await sut.WaitForPendingSaveAsync();
        }
    }

    [Fact]
    public async Task SavingSomethingUnrelated_LeavesTheApplicationsLanguageAlone()
    {
        // Applying a language swaps the process's culture, so an auto-save triggered by toggling a
        // checkbox must not touch it. It used to re-apply on every save, which meant every keystroke
        // in a settings box redressed the whole window - and, in the tests, leaked a language into
        // whatever ran next.
        var store = Substitute.For<IAppSettingsStore>();
        var settingsService = new AppSettingsService(new AppPaths(), store, new FileSystem());
        var localizationService = Substitute.For<ILocalizationService>();
        var sut = Create(settingsService, localizationService: localizationService);

        sut.EnableDht = !sut.EnableDht;
        await WaitForSaveAsync(store);
        await sut.WaitForPendingSaveAsync();

        localizationService.DidNotReceive().Apply(Arg.Any<string>());
    }

    [Fact]
    public async Task ChangingTheLanguage_AppliesItOnce()
    {
        var store = Substitute.For<IAppSettingsStore>();
        var settingsService = new AppSettingsService(new AppPaths(), store, new FileSystem());
        var localizationService = Substitute.For<ILocalizationService>();
        var sut = Create(settingsService, localizationService: localizationService);

        sut.SelectedLanguage = "sv-SE";
        await WaitForSaveAsync(store);
        await sut.WaitForPendingSaveAsync();

        localizationService.Received(1).Apply("sv-SE");
    }

    [Fact]
    public async Task LoadingTheScreen_WritesNothing()
    {
        // Reading the stored values into the view model is not the user changing anything.
        var store = Substitute.For<IAppSettingsStore>();
        var settingsService = new AppSettingsService(new AppPaths(), store, new FileSystem());

        Create(settingsService);
        await Task.Delay(700, TestContext.Current.CancellationToken);

        await store.DidNotReceive().SaveAsync(Arg.Any<AppSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AModeChangeMadeElsewhere_MovesTheButtonsHereToo()
    {
        // Simple mode has its own "switch to advanced" link. Answering it used to leave these
        // buttons showing the mode that had just been left behind.
        var interfaceModeService = Substitute.For<IInterfaceModeService>();
        interfaceModeService.IsSimple.Returns(true);
        var sut = Create(_settingsService, interfaceModeService);
        Assert.True(sut.IsSimpleMode);

        WeakReferenceMessenger.Default.Send(new InterfaceModeChangedMessage(InterfaceMode.Advanced));

        Assert.False(sut.IsSimpleMode);
        Assert.True(sut.IsAdvancedMode);
    }

    [Fact]
    public async Task ChoosingAMode_ShowsItBeforeWaitingOnTheSave()
    {
        var interfaceModeService = Substitute.For<IInterfaceModeService>();
        var saveStarted = new TaskCompletionSource();
        var releaseSave = new TaskCompletionSource();
        interfaceModeService
            .SetAsync(Arg.Any<InterfaceMode>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                saveStarted.TrySetResult();
                await releaseSave.Task;
            });

        var sut = Create(_settingsService, interfaceModeService);
        var switching = sut.SetInterfaceModeCommand.ExecuteAsync(InterfaceMode.Simple);
        await saveStarted.Task;

        // Still mid-save, and the interface has already moved.
        Assert.True(sut.IsSimpleMode);

        releaseSave.TrySetResult();
        await switching;
    }

    [Fact]
    public void CheckingAModeChip_SwitchesToThatMode()
    {
        // The chips bind IsChecked two ways rather than doing their work in a click handler, so that
        // choosing one through the accessibility API - which moves the dot without raising a click -
        // switches the mode as well as filling in the dot.
        var interfaceModeService = Substitute.For<IInterfaceModeService>();
        interfaceModeService.IsSimple.Returns(false);
        var sut = Create(_settingsService, interfaceModeService);
        Assert.True(sut.AdvancedModeSelected);

        sut.SimpleModeSelected = true;

        Assert.True(sut.IsSimpleMode);
        Assert.True(sut.SimpleModeSelected);
        Assert.False(sut.AdvancedModeSelected);
    }

    [Fact]
    public void UncheckingAModeChip_ChangesNothing()
    {
        // A choice arrives as one chip becoming true and the other false, in no guaranteed order.
        // Acting on the false would switch to the mode being left behind.
        var interfaceModeService = Substitute.For<IInterfaceModeService>();
        interfaceModeService.IsSimple.Returns(true);
        var sut = Create(_settingsService, interfaceModeService);

        sut.AdvancedModeSelected = false;

        Assert.True(sut.IsSimpleMode);
    }

    [Fact]
    public void CheckingTheModeAlreadyInForce_DoesNotSaveItAgain()
    {
        // The binding writes the property back whenever the dot moves, including when the mode was
        // changed somewhere else and this screen is only catching up.
        var interfaceModeService = Substitute.For<IInterfaceModeService>();
        interfaceModeService.IsSimple.Returns(true);
        var sut = Create(_settingsService, interfaceModeService);

        sut.SimpleModeSelected = true;

        interfaceModeService.DidNotReceive().SetAsync(Arg.Any<InterfaceMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SimpleMode_HidesTheAdvancedSettings()
    {
        var interfaceModeService = Substitute.For<IInterfaceModeService>();
        interfaceModeService.IsSimple.Returns(true);

        var sut = Create(_settingsService, interfaceModeService);

        Assert.True(sut.IsSimpleMode);
        Assert.False(sut.IsAdvancedMode);
    }

    private static async Task<bool> WaitForSaveAsync(IAppSettingsStore store)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (store.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IAppSettingsStore.SaveAsync)))
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    /// <summary>
    /// Shown in kibibytes per second because that is the unit every other client uses, stored in
    /// bytes because that is what the engine takes. The conversion is the only place that can go
    /// wrong, and getting it wrong by 1024 is not a subtle bug to live with.
    /// </summary>
    [Fact]
    public async Task ASpeedLimit_IsStoredInBytesAndShownInKibibytes()
    {
        _sut.MaxDownloadSpeedKibibytesPerSecond = 500;
        _sut.MaxUploadSpeedKibibytesPerSecond = 64;
        await _sut.SaveCommand.ExecuteAsync(null);

        Assert.Equal(500 * 1024, _settingsService.Current.Network.MaxDownloadSpeedBytesPerSecond);
        Assert.Equal(64 * 1024, _settingsService.Current.Network.MaxUploadSpeedBytesPerSecond);

        var reloaded = Create(_settingsService);

        Assert.Equal(500, reloaded.MaxDownloadSpeedKibibytesPerSecond);
        Assert.Equal(64, reloaded.MaxUploadSpeedKibibytesPerSecond);
    }

    [Fact]
    public void ANegativeSpeedLimit_IsRefused()
    {
        // Unsigned at the engine, so a negative would wrap into an enormous limit rather than none.
        _sut.MaxDownloadSpeedKibibytesPerSecond = -1;

        Assert.Equal(0, _sut.MaxDownloadSpeedKibibytesPerSecond);
    }

    /// <summary>
    /// The point of the setting: someone throttles mid-download to get out of the way of a video
    /// call. Restarting the engine to apply it would drop every connection they have.
    /// </summary>
    [Fact]
    public void ChangingASpeedLimit_ReachesTheRunningEngine()
    {
        var engineService = Substitute.For<ITorrentEngineService>();
        var sut = Create(_settingsService, engineService: engineService);
        engineService.ClearReceivedCalls();

        sut.MaxDownloadSpeedKibibytesPerSecond = 250;

        engineService.Received().ApplySpeedLimits();
    }

    private SettingsViewModel Create(
        IAppSettingsService settingsService,
        IInterfaceModeService? interfaceModeService = null,
        ILocalizationService? localizationService = null,
        ITorrentSearchService? searchService = null,
        ITorrentEngineService? engineService = null)
    {
        var updateLogger = Substitute.For<Microsoft.Extensions.Logging.ILogger<UpdateService>>();
        return new SettingsViewModel(
            settingsService,
            _themeService,
            localizationService ?? _localizationService,
            _topLevelService,
            engineService ?? new TorrentEngineService(settingsService, Substitute.For<Microsoft.Extensions.Logging.ILoggerFactory>()),
            new UpdateService(updateLogger, settingsService),
            _windowsAssociationService,
            interfaceModeService ?? Substitute.For<IInterfaceModeService>(),
            searchService ?? Substitute.For<ITorrentSearchService>(),
            Substitute.For<ITorrentCategoryService>());
    }

    [Fact]
    public async Task UseIndexerPreset_FillsInTheAddress_SoOnlyTheKeyIsLeftToSupply()
    {
        await _sut.UseIndexerPresetCommand.ExecuteAsync(SearchSettings.ProwlarrTemplate);

        Assert.Equal(SearchSettings.ProwlarrTemplate, _sut.TorznabUrl);
        Assert.True(_sut.HasSearchStatusMessage);
    }

    [Fact]
    public async Task UseIndexerPreset_IgnoresAnEmptyTemplate()
    {
        _sut.TorznabUrl = "http://example.invalid/api";

        await _sut.UseIndexerPresetCommand.ExecuteAsync(string.Empty);

        Assert.Equal("http://example.invalid/api", _sut.TorznabUrl);
    }

    [Fact]
    public async Task Detect_FillsInWhateverItFound()
    {
        var searchService = Substitute.For<ITorrentSearchService>();
        searchService.DetectLocalEndpointAsync(Arg.Any<CancellationToken>())
            .Returns(SearchSettings.JackettTemplate);
        var sut = Create(_settingsService, searchService: searchService);

        await sut.DetectIndexerCommand.ExecuteAsync(null);

        Assert.Equal(SearchSettings.JackettTemplate, sut.TorznabUrl);
        Assert.Equal(Peerfluence.Properties.Resources.Settings_Search_Detected, sut.SearchStatusMessage);
    }

    [Fact]
    public async Task Detect_LeavesTheAddressAlone_WhenNothingIsRunning()
    {
        var searchService = Substitute.For<ITorrentSearchService>();
        searchService.DetectLocalEndpointAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
        var sut = Create(_settingsService, searchService: searchService);
        sut.TorznabUrl = "http://example.invalid/api";

        await sut.DetectIndexerCommand.ExecuteAsync(null);

        Assert.Equal("http://example.invalid/api", sut.TorznabUrl);
        Assert.Equal(Peerfluence.Properties.Resources.Settings_Search_NotDetected, sut.SearchStatusMessage);
    }

    /// <summary>
    /// The service reads the endpoint from settings, so a Test pressed straight after typing must
    /// test what is in the box - not what the debounced save last wrote.
    /// </summary>
    [Fact]
    public async Task Test_ChecksTheAddressCurrentlyInTheBox()
    {
        var searchService = Substitute.For<ITorrentSearchService>();
        searchService.TestAsync(Arg.Any<CancellationToken>()).Returns(TorrentSearchResponse.Succeeded([]));
        var sut = Create(_settingsService, searchService: searchService);

        sut.TorznabUrl = "http://example.invalid/typed-just-now";
        await sut.TestIndexerCommand.ExecuteAsync(null);

        Assert.Equal("http://example.invalid/typed-just-now", _settingsService.Current.Search.TorznabUrl);
        Assert.Equal(Peerfluence.Properties.Resources.Settings_Search_TestPassed, sut.SearchStatusMessage);
    }

    /// <summary>
    /// The address, not the socket error. Someone reading this needs to know which port nothing is
    /// listening on, and that the thing to do about it is start the application that should be.
    /// </summary>
    [Fact]
    public async Task Test_NamesTheAddress_WhenNothingIsListening()
    {
        var searchService = Substitute.For<ITorrentSearchService>();
        searchService.TestAsync(Arg.Any<CancellationToken>())
            .Returns(TorrentSearchResponse.Failed(SearchFailure.Unreachable, "127.0.0.1:9117"));
        var sut = Create(_settingsService, searchService: searchService);

        await sut.TestIndexerCommand.ExecuteAsync(null);

        Assert.Contains("127.0.0.1:9117", sut.SearchStatusMessage);
        Assert.Contains("Jackett", sut.SearchStatusMessage);
    }

    [Fact]
    public async Task Test_SaysItIsTheKey_WhenTheIndexerAnswersAndRefuses()
    {
        var searchService = Substitute.For<ITorrentSearchService>();
        searchService.TestAsync(Arg.Any<CancellationToken>())
            .Returns(TorrentSearchResponse.Failed(SearchFailure.Rejected, "401 Unauthorized"));
        var sut = Create(_settingsService, searchService: searchService);

        await sut.TestIndexerCommand.ExecuteAsync(null);

        Assert.Equal(Peerfluence.Properties.Resources.Settings_Search_TestRejected, sut.SearchStatusMessage);
    }

    /// <summary>
    /// The whole reason the preset buttons exist is to save the user finding the address. Letting
    /// them walk to the search screen to discover the software is not running would give that back.
    /// </summary>
    [Fact]
    public async Task UseIndexerPreset_ChecksStraightAway_RatherThanLeavingItToBeDiscoveredLater()
    {
        var searchService = Substitute.For<ITorrentSearchService>();
        searchService.TestAsync(Arg.Any<CancellationToken>())
            .Returns(TorrentSearchResponse.Failed(SearchFailure.Unreachable, "127.0.0.1:9696"));
        var sut = Create(_settingsService, searchService: searchService);

        await sut.UseIndexerPresetCommand.ExecuteAsync(SearchSettings.ProwlarrTemplate);

        await searchService.Received(1).TestAsync(Arg.Any<CancellationToken>());
        Assert.Contains("127.0.0.1:9696", sut.SearchStatusMessage);
    }

    [Fact]
    public void ArrivingFromTheSearchScreen_OpensTheSearchTab()
    {
        Assert.NotEqual(SettingsViewModel.SearchTabIndex, _sut.SelectedTabIndex);

        WeakReferenceMessenger.Default.Send(new ShowSearchSettingsMessage());

        Assert.Equal(SettingsViewModel.SearchTabIndex, _sut.SelectedTabIndex);
    }

    [Fact]
    public async Task TheSearchEndpoint_SurvivesASaveAndReload()
    {
        _sut.TorznabUrl = "http://example.invalid/api";
        _sut.TorznabApiKey = "abc123";
        await _sut.SaveCommand.ExecuteAsync(null);

        var reloaded = Create(_settingsService);

        Assert.Equal("http://example.invalid/api", reloaded.TorznabUrl);
        Assert.Equal("abc123", reloaded.TorznabApiKey);
    }

    [Fact]
    public void WhetherUpdatesCanBeChecked_ComesFromTheUpdateService()
    {
        // These two decide whether the update section is offered at all. A build installed from a
        // package manager has no Velopack path, and showing it a "check for updates" button that
        // can never work is worse than showing nothing.
        var updateService = Substitute.For<IUpdateService>();
        updateService.IsInstalled.Returns(true);
        updateService.CanCheckForUpdates.Returns(true);

        var sut = CreateWith(updateService);

        Assert.True(sut.IsUpdateServiceInstalled);
        Assert.True(sut.CanCheckForUpdates);
    }

    [Fact]
    public void ABuildThatCannotUpdateItself_SaysSo()
    {
        var updateService = Substitute.For<IUpdateService>();
        updateService.IsInstalled.Returns(false);
        updateService.CanCheckForUpdates.Returns(false);

        var sut = CreateWith(updateService);

        Assert.False(sut.IsUpdateServiceInstalled);
        Assert.False(sut.CanCheckForUpdates);
    }

    private SettingsViewModel CreateWith(IUpdateService updateService)
    {
        var searchService = Substitute.For<ITorrentSearchService>();
        searchService.TestAsync(Arg.Any<CancellationToken>()).Returns(TorrentSearchResponse.Succeeded([]));

        return new SettingsViewModel(
            _settingsService,
            _themeService,
            _localizationService,
            _topLevelService,
            _engineService,
            updateService,
            _windowsAssociationService,
            Substitute.For<IInterfaceModeService>(),
            searchService,
            Substitute.For<ITorrentCategoryService>());
    }

}
