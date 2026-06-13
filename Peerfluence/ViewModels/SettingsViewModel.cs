using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;

namespace Peerfluence.ViewModels;

public sealed class SettingsViewModel : ViewModelBase, IFeatureViewModel
{
    private readonly IAppSettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localizationService;
    private readonly ITopLevelService _topLevelService;
    private readonly ITorrentEngineService _engineService;
    private readonly IUpdateService _updateService;
    private readonly IWindowsAssociationService _windowsAssociationService;
    private IReadOnlyList<SettingsOption> _themeVariantOptions = CreateThemeVariantOptions();
    private IReadOnlyList<SettingsOption> _colorThemeOptions = CreateColorThemeOptions();
    private IReadOnlyList<SettingsOption> _backgroundStyleOptions = CreateBackgroundStyleOptions();
    private IReadOnlyList<SettingsOption> _encryptionModeOptions = CreateEncryptionModeOptions();
    private IReadOnlyList<SettingsOption> _proxyTypeOptions = CreateProxyTypeOptions();

    public SettingsViewModel(
        IAppSettingsService settingsService,
        IThemeService themeService,
        ILocalizationService localizationService,
        ITopLevelService topLevelService,
        ITorrentEngineService engineService,
        IUpdateService updateService,
        IWindowsAssociationService windowsAssociationService)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _localizationService = localizationService;
        _topLevelService = topLevelService;
        _engineService = engineService;
        _updateService = updateService;
        _windowsAssociationService = windowsAssociationService;

        PortMappingStatuses = new ObservableCollection<PortMappingStatusViewModel>();

        LoadFromSettings();
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        ResetDefaultsCommand = new RelayCommand(ResetDefaults);
        BrowseBlocklistCommand = new AsyncRelayCommand(BrowseBlocklistAsync);
        BrowseGeoIpCommand = new AsyncRelayCommand(BrowseGeoIpAsync);
        BrowseMediaPlayerCommand = new AsyncRelayCommand(BrowseMediaPlayerAsync);
        BrowseCompletionActionProgramCommand = new AsyncRelayCommand(BrowseCompletionActionProgramAsync);
        BrowseDownloadPathCommand = new AsyncRelayCommand(BrowseDownloadPathAsync);
        BrowseSessionPathCommand = new AsyncRelayCommand(BrowseSessionPathAsync);
        RefreshPortMappingCommand = new RelayCommand(RefreshPortMapping);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        ApplyUpdateAndRestartCommand = new RelayCommand(ApplyUpdateAndRestart);
    }

    // IFeatureViewModel
    public string Title => Properties.Resources.Nav_Settings;

    public string IconKind => "CogOutline";

    public int Order => 100;

    // Storage
    public string DownloadPath
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string SessionPath
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public bool EnableSessionPersistence
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool ShowAddTorrentOptions
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool ShowRemoveTorrentOptions
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool AssociateTorrentFiles
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool AssociateMagnetLinks
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool CanManageWindowsAssociations => _windowsAssociationService.IsSupported;

    // Network
    public bool EnableDht
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool EnableNatPmp
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool EnableUpnp
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool UseAutomaticListeningPort
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsFixedListeningPortEnabled));
            }
        }
    }

    public bool IsFixedListeningPortEnabled => !UseAutomaticListeningPort;

    public int ListeningPort
    {
        get;
        set => SetProperty(ref field, Math.Clamp(value, 1, 65535));
    } = 55125;

    public long MaxDiskReadSpeedBytesPerSecond
    {
        get;
        set => SetProperty(ref field, Math.Max(0, value));
    }

    public long MaxDiskWriteSpeedBytesPerSecond
    {
        get;
        set => SetProperty(ref field, Math.Max(0, value));
    }

    // Theme
    public string SelectedThemeVariant
    {
        get;
        set => SetProperty(ref field, value);
    } = "System";

    public string SelectedColorTheme
    {
        get;
        set => SetProperty(ref field, value);
    } = "Indigo";

    public string SelectedBackgroundStyle
    {
        get;
        set => SetProperty(ref field, value);
    } = "GradientSoft";

    public string SelectedLanguage
    {
        get;
        set => SetProperty(ref field, value);
    } = "en-US";

    // Queue management
    public bool EnableQueueManagement
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int MaxActiveDownloads
    {
        get;
        set => SetProperty(ref field, Math.Max(0, value));
    } = 3;

    public int MaxActiveSeeds
    {
        get;
        set => SetProperty(ref field, Math.Max(0, value));
    } = 2;

    // Blocklist
    public bool EnableBlocklist
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string BlocklistPath
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    // GeoIP
    public bool EnableGeoIp
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string GeoIpPath
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    // Media Player
    public string MediaPlayerPath
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    // Completion action
    public bool CompletionActionEnabled
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string CompletionActionProgramPath
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string CompletionActionArgumentsTemplate
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string CompletionActionWorkingDirectoryTemplate
    {
        get;
        set => SetProperty(ref field, value);
    } = "{downloadPath}";

    public int CompletionActionTimeoutSeconds
    {
        get;
        set => SetProperty(ref field, Math.Max(1, value));
    } = 300;

    public bool CompletionActionRunHidden
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    // Encryption
    public string SelectedEncryptionMode
    {
        get;
        set => SetProperty(ref field, value);
    } = "Allow";

    // Proxy
    public string SelectedProxyType
    {
        get;
        set => SetProperty(ref field, value);
    } = "None";

    public string ProxyHost
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public int ProxyPort
    {
        get;
        set => SetProperty(ref field, Math.Clamp(value, 0, 65535));
    }

    public string ProxyUsername
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string ProxyPassword
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public bool ProxyPeers
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool ProxyTrackers
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    // Updates
    public string UpdateUrl
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public bool CheckForUpdatesOnStartup
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool IsUpdateAvailable
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string StatusMessage
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    } = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public IReadOnlyList<string> ThemeVariants { get; } = new[] { "System", "Light", "Dark" };

    public IReadOnlyList<SettingsOption> ThemeVariantOptions
    {
        get => _themeVariantOptions;
        private set => SetProperty(ref _themeVariantOptions, value);
    }

    public IReadOnlyList<string> ColorThemes { get; } = new[] { "Indigo", "Cobalt", "Mint", "Emerald", "Rose", "Vibrant", "Amber", "Slate", "Solar" };

    public IReadOnlyList<SettingsOption> ColorThemeOptions
    {
        get => _colorThemeOptions;
        private set => SetProperty(ref _colorThemeOptions, value);
    }

    public IReadOnlyList<string> BackgroundStyles { get; } = new[] { "GradientSoft", "Gradient", "GradientDarker", "Flat", "Bubble" };

    public IReadOnlyList<SettingsOption> BackgroundStyleOptions
    {
        get => _backgroundStyleOptions;
        private set => SetProperty(ref _backgroundStyleOptions, value);
    }

    public IReadOnlyList<SettingsOption> LanguageOptions { get; } = new[]
    {
        new SettingsOption("en-US", "English (en-US)"),
        new SettingsOption("sv-SE", "Svenska (sv-SE)"),
        new SettingsOption("es-ES", "Español (es-ES)"),
        new SettingsOption("de-DE", "Deutsch (de-DE)"),
        new SettingsOption("fr-FR", "Français (fr-FR)"),
        new SettingsOption("pl-PL", "Polski (pl-PL)"),
        new SettingsOption("it-IT", "Italiano (it-IT)"),
        new SettingsOption("pt-PT", "Português (pt-PT)"),
        new SettingsOption("ru-RU", "Русский (ru-RU)"),
        new SettingsOption("uk-UA", "Українська (uk-UA)")
    };

    public IReadOnlyList<string> Languages => LanguageOptions.Select(option => option.Value).ToArray();

    public IReadOnlyList<string> EncryptionModes { get; } = new[] { "Allow", "Require", "Refuse" };

    public IReadOnlyList<SettingsOption> EncryptionModeOptions
    {
        get => _encryptionModeOptions;
        private set => SetProperty(ref _encryptionModeOptions, value);
    }

    public IReadOnlyList<string> ProxyTypes { get; } = new[] { "None", "Socks5", "Http" };

    public IReadOnlyList<SettingsOption> ProxyTypeOptions
    {
        get => _proxyTypeOptions;
        private set => SetProperty(ref _proxyTypeOptions, value);
    }

    public IAsyncRelayCommand SaveCommand { get; }

    public IRelayCommand ResetDefaultsCommand { get; }

    public IAsyncRelayCommand BrowseBlocklistCommand { get; }

    public IAsyncRelayCommand BrowseGeoIpCommand { get; }

    public IAsyncRelayCommand BrowseMediaPlayerCommand { get; }

    public IAsyncRelayCommand BrowseCompletionActionProgramCommand { get; }

    public IAsyncRelayCommand BrowseDownloadPathCommand { get; }

    public IAsyncRelayCommand BrowseSessionPathCommand { get; }

    public IRelayCommand RefreshPortMappingCommand { get; }

    public IAsyncRelayCommand CheckForUpdatesCommand { get; }

    public IRelayCommand ApplyUpdateAndRestartCommand { get; }

    public bool IsUpdateServiceInstalled => _updateService.IsInstalled;

    public bool IsDirectDownloadUpdateChannel => _updateService.Channel == UpdateChannel.DirectDownload;

    public bool CanCheckForUpdates => _updateService.CanCheckForUpdates;

    public bool ShouldShowUpdateNotInstalled => !_updateService.IsInstalled;

    public string UpdateManagementMessage => Properties.Resources.Settings_UpdateNotInstalled;

    public ObservableCollection<PortMappingStatusViewModel> PortMappingStatuses { get; }

    private void LoadFromSettings()
    {
        var settings = _settingsService.Current;

        // Storage
        DownloadPath = settings.Storage.DownloadPath;
        SessionPath = settings.Storage.SessionPath;
        EnableSessionPersistence = settings.Storage.EnableSessionPersistence;
        ShowAddTorrentOptions = settings.ShowAddTorrentOptions;
        ShowRemoveTorrentOptions = settings.ShowRemoveTorrentOptions;
        AssociateTorrentFiles = _windowsAssociationService.IsSupported
            ? _windowsAssociationService.IsTorrentFileAssociated
            : settings.AssociateTorrentFiles;
        AssociateMagnetLinks = _windowsAssociationService.IsSupported
            ? _windowsAssociationService.IsMagnetLinkAssociated
            : settings.AssociateMagnetLinks;

        // Network
        EnableDht = settings.Network.EnableDht;
        EnableNatPmp = settings.Network.EnableNatPmp;
        EnableUpnp = settings.Network.EnableUpnp;
        UseAutomaticListeningPort = settings.Network.UseAutomaticListeningPort;
        ListeningPort = settings.Network.ListeningPort;
        MaxDiskReadSpeedBytesPerSecond = settings.Network.MaxDiskReadSpeedBytesPerSecond;
        MaxDiskWriteSpeedBytesPerSecond = settings.Network.MaxDiskWriteSpeedBytesPerSecond;

        // Theme
        SelectedThemeVariant = settings.Theme.ThemeVariant;
        SelectedColorTheme = settings.Theme.ColorTheme;
        SelectedBackgroundStyle = settings.Theme.BackgroundStyle;
        SelectedLanguage = settings.Language;

        // Queue
        EnableQueueManagement = settings.Queue.EnableQueueManagement;
        MaxActiveDownloads = settings.Queue.MaxActiveDownloads;
        MaxActiveSeeds = settings.Queue.MaxActiveSeeds;

        // Misc
        EnableBlocklist = settings.EnableBlocklist;
        BlocklistPath = settings.BlocklistPath;
        EnableGeoIp = settings.EnableGeoIp;
        GeoIpPath = settings.GeoIpPath;
        MediaPlayerPath = settings.MediaPlayerPath;
        SelectedEncryptionMode = settings.EncryptionMode;
        CompletionActionEnabled = settings.CompletionAction.Enabled;
        CompletionActionProgramPath = settings.CompletionAction.ProgramPath;
        CompletionActionArgumentsTemplate = settings.CompletionAction.ArgumentsTemplate;
        CompletionActionWorkingDirectoryTemplate = settings.CompletionAction.WorkingDirectoryTemplate;
        CompletionActionTimeoutSeconds = settings.CompletionAction.TimeoutSeconds;
        CompletionActionRunHidden = settings.CompletionAction.RunHidden;

        // Proxy
        SelectedProxyType = settings.Proxy.ProxyType;
        ProxyHost = settings.Proxy.ProxyHost;
        ProxyPort = settings.Proxy.ProxyPort;
        ProxyUsername = settings.Proxy.ProxyUsername;
        ProxyPassword = settings.Proxy.ProxyPassword;
        ProxyPeers = settings.Proxy.ProxyPeers;
        ProxyTrackers = settings.Proxy.ProxyTrackers;

        // Updates
        UpdateUrl = settings.Update.UpdateUrl;
        CheckForUpdatesOnStartup = settings.Update.CheckForUpdatesOnStartup;
    }

    private async Task SaveAsync()
    {
        try
        {
            var settings = _settingsService.Current;

            // Storage
            settings.Storage.DownloadPath = DownloadPath;
            settings.Storage.SessionPath = SessionPath;
            settings.Storage.EnableSessionPersistence = EnableSessionPersistence;
            settings.ShowAddTorrentOptions = ShowAddTorrentOptions;
            settings.ShowRemoveTorrentOptions = ShowRemoveTorrentOptions;
            settings.AssociateTorrentFiles = AssociateTorrentFiles;
            settings.AssociateMagnetLinks = AssociateMagnetLinks;

            // Network
            settings.Network.EnableDht = EnableDht;
            settings.Network.EnableNatPmp = EnableNatPmp;
            settings.Network.EnableUpnp = EnableUpnp;
            settings.Network.UseAutomaticListeningPort = UseAutomaticListeningPort;
            settings.Network.ListeningPort = ListeningPort;
            settings.Network.MaxDiskReadSpeedBytesPerSecond = MaxDiskReadSpeedBytesPerSecond;
            settings.Network.MaxDiskWriteSpeedBytesPerSecond = MaxDiskWriteSpeedBytesPerSecond;

            // Theme
            settings.Theme.ThemeVariant = SelectedThemeVariant;
            settings.Theme.ColorTheme = SelectedColorTheme;
            settings.Theme.BackgroundStyle = SelectedBackgroundStyle;
            settings.Language = SelectedLanguage;

            // Queue
            settings.Queue.EnableQueueManagement = EnableQueueManagement;
            settings.Queue.MaxActiveDownloads = MaxActiveDownloads;
            settings.Queue.MaxActiveSeeds = MaxActiveSeeds;

            // Misc
            settings.EnableBlocklist = EnableBlocklist;
            settings.BlocklistPath = BlocklistPath;
            settings.EnableGeoIp = EnableGeoIp;
            settings.GeoIpPath = GeoIpPath;
            settings.MediaPlayerPath = MediaPlayerPath;
            settings.EncryptionMode = SelectedEncryptionMode;
            settings.CompletionAction.Enabled = CompletionActionEnabled;
            settings.CompletionAction.ProgramPath = CompletionActionProgramPath;
            settings.CompletionAction.ArgumentsTemplate = CompletionActionArgumentsTemplate;
            settings.CompletionAction.WorkingDirectoryTemplate = CompletionActionWorkingDirectoryTemplate;
            settings.CompletionAction.TimeoutSeconds = CompletionActionTimeoutSeconds;
            settings.CompletionAction.RunHidden = CompletionActionRunHidden;

            // Proxy
            settings.Proxy.ProxyType = SelectedProxyType;
            settings.Proxy.ProxyHost = ProxyHost;
            settings.Proxy.ProxyPort = ProxyPort;
            settings.Proxy.ProxyUsername = ProxyUsername;
            settings.Proxy.ProxyPassword = ProxyPassword;
            settings.Proxy.ProxyPeers = ProxyPeers;
            settings.Proxy.ProxyTrackers = ProxyTrackers;

            // Updates
            settings.Update.UpdateUrl = UpdateUrl;
            settings.Update.CheckForUpdatesOnStartup = CheckForUpdatesOnStartup;

            await _settingsService.SaveAsync(default);
            _windowsAssociationService.ApplyAssociations(AssociateTorrentFiles, AssociateMagnetLinks);
            _themeService.Apply(settings.Theme);
            _localizationService.Apply(settings.Language);
            NotifyLocalizedOptionsChanged();
            StatusMessage = Properties.Resources.Status_SettingsSaved;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Properties.Resources.Status_SettingsSaveFailed, ex.Message);
        }
    }

    private void ResetDefaults()
    {
        var defaults = _settingsService.CreateDefaultSettings();

        DownloadPath = defaults.Storage.DownloadPath;
        SessionPath = defaults.Storage.SessionPath;
        EnableSessionPersistence = defaults.Storage.EnableSessionPersistence;
        ShowAddTorrentOptions = defaults.ShowAddTorrentOptions;
        ShowRemoveTorrentOptions = defaults.ShowRemoveTorrentOptions;
        AssociateTorrentFiles = defaults.AssociateTorrentFiles;
        AssociateMagnetLinks = defaults.AssociateMagnetLinks;
        EnableDht = defaults.Network.EnableDht;
        EnableNatPmp = defaults.Network.EnableNatPmp;
        EnableUpnp = defaults.Network.EnableUpnp;
        UseAutomaticListeningPort = defaults.Network.UseAutomaticListeningPort;
        ListeningPort = defaults.Network.ListeningPort;
        MaxDiskReadSpeedBytesPerSecond = defaults.Network.MaxDiskReadSpeedBytesPerSecond;
        MaxDiskWriteSpeedBytesPerSecond = defaults.Network.MaxDiskWriteSpeedBytesPerSecond;
        SelectedThemeVariant = defaults.Theme.ThemeVariant;
        SelectedColorTheme = defaults.Theme.ColorTheme;
        SelectedBackgroundStyle = defaults.Theme.BackgroundStyle;
        SelectedLanguage = defaults.Language;
        EnableQueueManagement = defaults.Queue.EnableQueueManagement;
        MaxActiveDownloads = defaults.Queue.MaxActiveDownloads;
        MaxActiveSeeds = defaults.Queue.MaxActiveSeeds;
        EnableBlocklist = defaults.EnableBlocklist;
        BlocklistPath = defaults.BlocklistPath;
        EnableGeoIp = defaults.EnableGeoIp;
        GeoIpPath = defaults.GeoIpPath;
        MediaPlayerPath = defaults.MediaPlayerPath;
        SelectedEncryptionMode = defaults.EncryptionMode;
        CompletionActionEnabled = defaults.CompletionAction.Enabled;
        CompletionActionProgramPath = defaults.CompletionAction.ProgramPath;
        CompletionActionArgumentsTemplate = defaults.CompletionAction.ArgumentsTemplate;
        CompletionActionWorkingDirectoryTemplate = defaults.CompletionAction.WorkingDirectoryTemplate;
        CompletionActionTimeoutSeconds = defaults.CompletionAction.TimeoutSeconds;
        CompletionActionRunHidden = defaults.CompletionAction.RunHidden;
        SelectedProxyType = defaults.Proxy.ProxyType;
        ProxyHost = defaults.Proxy.ProxyHost;
        ProxyPort = defaults.Proxy.ProxyPort;
        ProxyUsername = defaults.Proxy.ProxyUsername;
        ProxyPassword = defaults.Proxy.ProxyPassword;
        ProxyPeers = defaults.Proxy.ProxyPeers;
        ProxyTrackers = defaults.Proxy.ProxyTrackers;
        UpdateUrl = defaults.Update.UpdateUrl;
        CheckForUpdatesOnStartup = defaults.Update.CheckForUpdatesOnStartup;
        StatusMessage = Properties.Resources.Status_SettingsReset;
    }

    private async Task CheckForUpdatesAsync()
    {
        if (!_updateService.CanCheckForUpdates)
        {
            StatusMessage = UpdateManagementMessage;
            return;
        }

        StatusMessage = string.Empty;
        var hasUpdate = await _updateService.CheckForUpdatesAsync();
        if (hasUpdate)
        {
            StatusMessage = Properties.Resources.Status_DownloadingUpdate;
            var downloaded = await _updateService.DownloadUpdateAsync();
            if (downloaded)
            {
                IsUpdateAvailable = true;
                StatusMessage = Properties.Resources.Status_UpdateReady;
            }
            else
            {
                StatusMessage = Properties.Resources.Status_UpdateCheckFailed;
            }
        }
        else
        {
            StatusMessage = Properties.Resources.Status_NoUpdatesAvailable;
        }
    }

    private void ApplyUpdateAndRestart()
    {
        if (!_updateService.CanApplyUpdates)
        {
            StatusMessage = UpdateManagementMessage;
            return;
        }

        _updateService.ApplyUpdateAndRestart();
    }

    private void NotifyLocalizedOptionsChanged()
    {
        var selectedThemeVariant = SelectedThemeVariant;
        var selectedColorTheme = SelectedColorTheme;
        var selectedBackgroundStyle = SelectedBackgroundStyle;
        var selectedEncryptionMode = SelectedEncryptionMode;
        var selectedProxyType = SelectedProxyType;

        ThemeVariantOptions = CreateThemeVariantOptions();
        ColorThemeOptions = CreateColorThemeOptions();
        BackgroundStyleOptions = CreateBackgroundStyleOptions();
        EncryptionModeOptions = CreateEncryptionModeOptions();
        ProxyTypeOptions = CreateProxyTypeOptions();

        SelectedThemeVariant = selectedThemeVariant;
        SelectedColorTheme = selectedColorTheme;
        SelectedBackgroundStyle = selectedBackgroundStyle;
        SelectedEncryptionMode = selectedEncryptionMode;
        SelectedProxyType = selectedProxyType;
        OnPropertyChanged(nameof(SelectedThemeVariant));
        OnPropertyChanged(nameof(SelectedColorTheme));
        OnPropertyChanged(nameof(SelectedBackgroundStyle));
        OnPropertyChanged(nameof(SelectedEncryptionMode));
        OnPropertyChanged(nameof(SelectedProxyType));
        OnPropertyChanged(nameof(UpdateManagementMessage));
        foreach (var status in PortMappingStatuses)
        {
            status.RefreshLocalizedText();
        }
    }

    private static IReadOnlyList<SettingsOption> CreateThemeVariantOptions()
    {
        return new[]
        {
            new SettingsOption("System", Properties.Resources.Settings_ThemeVariant_System),
            new SettingsOption("Light", Properties.Resources.Settings_ThemeVariant_Light),
            new SettingsOption("Dark", Properties.Resources.Settings_ThemeVariant_Dark)
        };
    }

    private static IReadOnlyList<SettingsOption> CreateColorThemeOptions()
    {
        return new[]
        {
            new SettingsOption("Indigo", Properties.Resources.Settings_ColorTheme_Indigo),
            new SettingsOption("Cobalt", Properties.Resources.Settings_ColorTheme_Cobalt),
            new SettingsOption("Mint", Properties.Resources.Settings_ColorTheme_Mint),
            new SettingsOption("Emerald", Properties.Resources.Settings_ColorTheme_Emerald),
            new SettingsOption("Rose", Properties.Resources.Settings_ColorTheme_Rose),
            new SettingsOption("Vibrant", Properties.Resources.Settings_ColorTheme_Vibrant),
            new SettingsOption("Amber", Properties.Resources.Settings_ColorTheme_Amber),
            new SettingsOption("Slate", Properties.Resources.Settings_ColorTheme_Slate),
            new SettingsOption("Solar", Properties.Resources.Settings_ColorTheme_Solar)
        };
    }

    private static IReadOnlyList<SettingsOption> CreateBackgroundStyleOptions()
    {
        return new[]
        {
            new SettingsOption("GradientSoft", Properties.Resources.Settings_BackgroundStyle_GradientSoft),
            new SettingsOption("Gradient", Properties.Resources.Settings_BackgroundStyle_Gradient),
            new SettingsOption("GradientDarker", Properties.Resources.Settings_BackgroundStyle_GradientDarker),
            new SettingsOption("Flat", Properties.Resources.Settings_BackgroundStyle_Flat),
            new SettingsOption("Bubble", Properties.Resources.Settings_BackgroundStyle_Bubble)
        };
    }

    private static IReadOnlyList<SettingsOption> CreateEncryptionModeOptions()
    {
        return new[]
        {
            new SettingsOption("Allow", Properties.Resources.Settings_EncryptionMode_Allow),
            new SettingsOption("Require", Properties.Resources.Settings_EncryptionMode_Require),
            new SettingsOption("Refuse", Properties.Resources.Settings_EncryptionMode_Refuse)
        };
    }

    private static IReadOnlyList<SettingsOption> CreateProxyTypeOptions()
    {
        return new[]
        {
            new SettingsOption("None", Properties.Resources.Settings_ProxyType_None),
            new SettingsOption("Socks5", Properties.Resources.Settings_ProxyType_Socks5),
            new SettingsOption("Http", Properties.Resources.Settings_ProxyType_Http)
        };
    }

    private async Task BrowseBlocklistAsync()
    {
        var storageProvider = _topLevelService.GetStorageProvider();

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Properties.Resources.Settings_BlocklistPicker_Title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(Properties.Resources.Settings_BlocklistPicker_Filter)
                {
                    Patterns = new[] { "*.txt", "*.p2p", "*.dat", "*.gz" }
                },
                new FilePickerFileType(Properties.Resources.Settings_BlocklistPicker_AllFiles)
                {
                    Patterns = new[] { "*" }
                }
            }
        });

        var file = files.FirstOrDefault();
        if (file != null)
        {
            BlocklistPath = file.Path.LocalPath;
        }
    }

    private void RefreshPortMapping()
    {
        try
        {
            var statuses = _engineService.Engine.GetPortMappingStatus();
            PortMappingStatuses.Clear();
            foreach (var status in statuses)
            {
                PortMappingStatuses.Add(new PortMappingStatusViewModel(status));
            }
        }
        catch
        {
            PortMappingStatuses.Clear();
        }
    }

    private async Task BrowseGeoIpAsync()
    {
        var storageProvider = _topLevelService.GetStorageProvider();

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Properties.Resources.Settings_GeoIpPicker_Title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(Properties.Resources.Settings_GeoIpPicker_Filter)
                {
                    Patterns = new[] { "*.mmdb", "*.dat", "*.csv" }
                },
                new FilePickerFileType(Properties.Resources.Settings_BlocklistPicker_AllFiles)
                {
                    Patterns = new[] { "*" }
                }
            }
        });

        var file = files.FirstOrDefault();
        if (file != null)
        {
            GeoIpPath = file.Path.LocalPath;
        }
    }

    private async Task BrowseMediaPlayerAsync()
    {
        var storageProvider = _topLevelService.GetStorageProvider();

        var filter = OperatingSystem.IsWindows()
            ? new[]
            {
                new FilePickerFileType(Properties.Resources.Settings_MediaPlayerPicker_Filter)
                {
                    Patterns = new[] { "*.exe", "*.bat", "*.cmd" }
                },
                new FilePickerFileType(Properties.Resources.Settings_BlocklistPicker_AllFiles)
                {
                    Patterns = new[] { "*" }
                }
            }
            : new[]
            {
                new FilePickerFileType(Properties.Resources.Settings_BlocklistPicker_AllFiles)
                {
                    Patterns = new[] { "*" }
                }
            };

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Properties.Resources.Settings_MediaPlayerPicker_Title,
            AllowMultiple = false,
            FileTypeFilter = filter
        });

        var file = files.FirstOrDefault();
        if (file != null)
        {
            MediaPlayerPath = file.Path.LocalPath;
        }
    }

    private async Task BrowseCompletionActionProgramAsync()
    {
        var storageProvider = _topLevelService.GetStorageProvider();

        var filter = OperatingSystem.IsWindows()
            ? new[]
            {
                new FilePickerFileType(Properties.Resources.Settings_CompletionActionPickerFilter)
                {
                    Patterns = new[] { "*.exe", "*.bat", "*.cmd", "*.ps1" }
                },
                new FilePickerFileType(Properties.Resources.Settings_BlocklistPicker_AllFiles)
                {
                    Patterns = new[] { "*" }
                }
            }
            : new[]
            {
                new FilePickerFileType(Properties.Resources.Settings_CompletionActionPickerFilter)
                {
                    Patterns = new[] { "*.sh", "*" }
                }
            };

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Properties.Resources.Settings_CompletionActionPickerTitle,
            AllowMultiple = false,
            FileTypeFilter = filter
        });

        var file = files.FirstOrDefault();
        if (file != null)
        {
            CompletionActionProgramPath = file.Path.LocalPath;
        }
    }

    private async Task BrowseDownloadPathAsync()
    {
        var storageProvider = _topLevelService.GetStorageProvider();

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Properties.Resources.Settings_DownloadFolderPicker_Title,
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder != null)
        {
            DownloadPath = folder.Path.LocalPath;
        }
    }

    private async Task BrowseSessionPathAsync()
    {
        var storageProvider = _topLevelService.GetStorageProvider();

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Properties.Resources.Settings_SessionFolderPicker_Title,
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder != null)
        {
            SessionPath = folder.Path.LocalPath;
        }
    }
}

public sealed record SettingsOption(string Value, string DisplayName);
