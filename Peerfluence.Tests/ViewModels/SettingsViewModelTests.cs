using System.IO.Abstractions;
using System.Globalization;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Peerfluence.ViewModels;

namespace Peerfluence.Tests.ViewModels;

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

        _sut = new SettingsViewModel(
            _settingsService,
            _themeService,
            _localizationService,
            _topLevelService,
            _engineService,
            updateService,
            _windowsAssociationService);
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

        Assert.NotEqual(_settingsService.Current.Storage.DownloadPath, _sut.DownloadPath);
        Assert.Equal(UpdateSettings.DefaultUpdateUrl, _sut.UpdateUrl);
        Assert.True(_sut.EnableDht);
        Assert.False(_sut.UseAutomaticListeningPort);
        Assert.Equal(55125, _sut.ListeningPort);
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
        _sut.DownloadPath = "/test/path";
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

        Assert.Equal("/test/path", _settingsService.Current.Storage.DownloadPath);
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
}
