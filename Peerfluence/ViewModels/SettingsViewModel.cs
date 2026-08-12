using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Core.Config;
using Peerfluence.Core.Messaging;

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

    /// <summary>
    /// Long enough to swallow a burst of changes - a slider being dragged, or Reset writing every
    /// field. Nothing is riding on it being short: the change is already in force by the time this
    /// starts, and the save on shutdown writes it whether or not this has come round.
    /// </summary>
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly IInterfaceModeService _interfaceModeService;
    private CancellationTokenSource? _autoSaveCts;
    private Task _pendingSave = Task.CompletedTask;
    private bool _suspendAutoSave;
    private string? _appliedLanguage;
    private string? _appliedTheme;
    private (bool Torrents, bool Magnets)? _appliedAssociations;

    public SettingsViewModel(
        IAppSettingsService settingsService,
        IThemeService themeService,
        ILocalizationService localizationService,
        ITopLevelService topLevelService,
        ITorrentEngineService engineService,
        IUpdateService updateService,
        IWindowsAssociationService windowsAssociationService,
        IInterfaceModeService interfaceModeService)
    {
        _interfaceModeService = interfaceModeService;
        _settingsService = settingsService;
        _themeService = themeService;
        _localizationService = localizationService;
        _topLevelService = topLevelService;
        _engineService = engineService;
        _updateService = updateService;
        _windowsAssociationService = windowsAssociationService;

        PortMappingStatuses = new ObservableCollection<PortMappingStatusViewModel>();

        LoadFromSettings();

        // What is already in force when the screen opens, so the first change applies only itself.
        _appliedTheme = CurrentThemeKey();
        _appliedAssociations = (AssociateTorrentFiles, AssociateMagnetLinks);
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

        SetInterfaceModeCommand = new AsyncRelayCommand<InterfaceMode>(SetInterfaceModeAsync);

        WeakReferenceMessenger.Default.Register<InterfaceModeChangedMessage>(this, (_, msg) => ApplyInterfaceMode(msg.Mode));

        PropertyChanged += OnSettingChanged;
    }

    /// <summary>
    /// The way back to simple mode, and the way to simple mode for anyone who chose advanced at the
    /// welcome. Goes through the mode service rather than the settings save, because the shell
    /// listens for the change to swap what it is showing.
    /// </summary>
    public IAsyncRelayCommand<InterfaceMode> SetInterfaceModeCommand { get; }

    private async Task SetInterfaceModeAsync(InterfaceMode mode)
    {
        // Shown before saved. Persisting means writing a file, and making the interface wait on
        // that is what made switching feel like a stall.
        ApplyInterfaceMode(mode);
        await _interfaceModeService.SetAsync(mode);
    }

    /// <summary>
    /// Brings the two mode buttons in line with the mode actually in force.
    ///
    /// <para>
    /// Needed because this screen is not the only way to change it: simple mode has its own
    /// "switch to advanced" link, and answering it left the buttons here showing the mode that had
    /// just been left behind.
    /// </para>
    /// </summary>
    private void ApplyInterfaceMode(InterfaceMode mode)
    {
        IsSimpleMode = mode == InterfaceMode.Simple;
        OnPropertyChanged(nameof(IsAdvancedMode));
    }

    /// <summary>
    /// Properties that are shown but never stored. Everything else on this view model is a setting,
    /// so changing it is what triggers the save.
    /// </summary>
    private static readonly HashSet<string> NotSettings = new(StringComparer.Ordinal)
    {
        nameof(StatusMessage),
        nameof(HasStatusMessage),
        nameof(IsUpdateAvailable),
        nameof(IsFixedListeningPortEnabled),
        nameof(ApplicationVersion),
        nameof(Title),
        nameof(IsSimpleMode),
        nameof(IsAdvancedMode),
        nameof(ThemeVariantOptions),
        nameof(ColorThemeOptions),
        nameof(BackgroundStyleOptions),
        nameof(EncryptionModeOptions),
        nameof(ProxyTypeOptions)
    };

    /// <summary>
    /// Saves what changed, shortly after it changes.
    ///
    /// <para>
    /// Windows 11 Settings does away with a Save button, and so does this: a settings screen that
    /// can be left in an unsaved state is a screen that can silently lose what you told it. The
    /// delay coalesces a burst - dragging a slider, or Reset writing every field at once - into one
    /// write, and success is deliberately silent. Only a failure is worth a message.
    /// </para>
    /// </summary>
    private void OnSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suspendAutoSave || e.PropertyName is null || NotSettings.Contains(e.PropertyName))
        {
            return;
        }

        // In force at once; on disk shortly. The settings object is what the rest of the
        // application reads and what the save on shutdown writes, so the change cannot be lost by
        // closing the window before the delayed write comes round.
        ApplyToSettings();
        ApplySideEffects();

        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _autoSaveCts = cts;

        _pendingSave = AutoSaveAsync(cts);
    }

    /// <summary>
    /// Waits for a scheduled save to finish, so a caller can be sure nothing is still in flight.
    ///
    /// <para>
    /// Exists because a debounced save outlives the change that scheduled it: a language change
    /// applies the process's culture when the save lands, which can be well after whoever made the
    /// change has moved on.
    /// </para>
    /// </summary>
    internal Task WaitForPendingSaveAsync() => _pendingSave;

    private async Task AutoSaveAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(AutoSaveDelay, cts.Token).ConfigureAwait(true);
            await PersistAsync(announce: false).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later change; that one will do the saving.
        }
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

    public bool AnswerInfoHashSampling
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool AllowMultipleConnectionsPerIp
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

    /// <summary>
    /// In simple mode the screen shows only where downloads go and how the app looks. Everything
    /// else is still there and still in force - it is hidden, not turned off.
    /// </summary>
    public bool IsSimpleMode
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool IsAdvancedMode => !IsSimpleMode;

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

    public string ApplicationVersion => ApplicationVersionInfo.Version;

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

    public IReadOnlyList<string> ThemeVariants { get; } = ["System", "Light", "Dark"];

    public IReadOnlyList<SettingsOption> ThemeVariantOptions
    {
        get => _themeVariantOptions;
        private set => SetProperty(ref _themeVariantOptions, value);
    }

    public IReadOnlyList<string> ColorThemes { get; } = ["Indigo", "Cobalt", "Mint", "Emerald", "Rose", "Vibrant", "Amber", "Slate", "Solar"];

    public IReadOnlyList<SettingsOption> ColorThemeOptions
    {
        get => _colorThemeOptions;
        private set => SetProperty(ref _colorThemeOptions, value);
    }

    public IReadOnlyList<string> BackgroundStyles { get; } = ["GradientSoft", "Gradient", "GradientDarker", "Flat", "Bubble"];

    public IReadOnlyList<SettingsOption> BackgroundStyleOptions
    {
        get => _backgroundStyleOptions;
        private set => SetProperty(ref _backgroundStyleOptions, value);
    }

    public IReadOnlyList<SettingsOption> LanguageOptions { get; } =
    [
        new SettingsOption("de-DE", "Deutsch (de-DE)"),
        new SettingsOption("en-US", "English (en-US)"),
        new SettingsOption("es-ES", "Español (es-ES)"),
        new SettingsOption("fr-FR", "Français (fr-FR)"),
        new SettingsOption("it-IT", "Italiano (it-IT)"),
        new SettingsOption("pl-PL", "Polski (pl-PL)"),
        new SettingsOption("pt-PT", "Português (pt-PT)"),
        new SettingsOption("ru-RU", "Русский (ru-RU)"),
        new SettingsOption("sv-SE", "Svenska (sv-SE)"),
        new SettingsOption("uk-UA", "Українська (uk-UA)")
    ];

    public IReadOnlyList<string> Languages => LanguageOptions.Select(option => option.Value).ToArray();

    public IReadOnlyList<string> EncryptionModes { get; } = ["Allow", "Require", "Refuse"];

    public IReadOnlyList<SettingsOption> EncryptionModeOptions
    {
        get => _encryptionModeOptions;
        private set => SetProperty(ref _encryptionModeOptions, value);
    }

    public IReadOnlyList<string> ProxyTypes { get; } = ["None", "Socks5", "Http"];

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
        // Reading the stored values into the view model is not the user changing anything, so it
        // must not start a save of what was just read.
        _suspendAutoSave = true;
        try
        {
            LoadFromSettingsCore();
        }
        finally
        {
            _suspendAutoSave = false;
        }
    }

    private string CurrentThemeKey()
    {
        return $"{SelectedThemeVariant}|{SelectedColorTheme}|{SelectedBackgroundStyle}";
    }

    private void LoadFromSettingsCore()
    {
        var settings = _settingsService.Current;
        IsSimpleMode = _interfaceModeService.IsSimple;

        // What is already in force, so the first save does not re-apply what was loaded.
        _appliedLanguage = settings.Language;

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
        AnswerInfoHashSampling = settings.Network.AnswerInfoHashSampling;
        AllowMultipleConnectionsPerIp = settings.Network.AllowMultipleConnectionsPerIp;
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

    private Task SaveAsync() => SaveAsync(announce: true);

    private async Task SaveAsync(bool announce)
    {
        ApplyToSettings();
        await PersistAsync(announce).ConfigureAwait(true);
    }

    /// <summary>
    /// Copies what is on screen into the settings object. Costs nothing - no disk, no side effects -
    /// which is why it happens the moment something changes rather than when the write does.
    ///
    /// <para>
    /// It is also what makes the write safe to delay. The shutdown save works from this object, as
    /// does every other part of the application that reads its settings, so a change is in force
    /// the moment it is made even if it reaches the disk a fraction of a second later.
    /// </para>
    /// </summary>
    private void ApplyToSettings()
    {
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
            settings.Network.AnswerInfoHashSampling = AnswerInfoHashSampling;
            settings.Network.AllowMultipleConnectionsPerIp = AllowMultipleConnectionsPerIp;
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
        }
    }

    /// <summary>
    /// Applies the things a settings change does beyond storing a value.
    ///
    /// <para>
    /// Each is guarded by whether it actually changed. They reach outside this screen - the theme
    /// repaints the application, the language swaps the process's culture, the associations write
    /// to the registry - and with a save on every property change, doing them unconditionally meant
    /// redressing the whole window because someone typed a character into a path box.
    /// </para>
    /// </summary>
    private void ApplySideEffects()
    {
        var settings = _settingsService.Current;

        if (_appliedAssociations != (AssociateTorrentFiles, AssociateMagnetLinks))
        {
            _appliedAssociations = (AssociateTorrentFiles, AssociateMagnetLinks);
            _windowsAssociationService.ApplyAssociations(AssociateTorrentFiles, AssociateMagnetLinks);
        }

        if (_appliedTheme != CurrentThemeKey())
        {
            _appliedTheme = CurrentThemeKey();
            _themeService.Apply(settings.Theme);
        }

        if (!string.Equals(_appliedLanguage, settings.Language, StringComparison.Ordinal))
        {
            _appliedLanguage = settings.Language;
            _localizationService.Apply(settings.Language);
            NotifyLocalizedOptionsChanged();
        }
    }

    /// <summary>
    /// Writes the settings to disk. The only part of saving worth delaying, because it is the only
    /// part that costs anything.
    /// </summary>
    private async Task PersistAsync(bool announce)
    {
        try
        {
            ApplySideEffects();
            await _settingsService.SaveAsync(default).ConfigureAwait(true);

            if (announce)
            {
                StatusMessage = Properties.Resources.Status_SettingsSaved;
            }
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
        AnswerInfoHashSampling = defaults.Network.AnswerInfoHashSampling;
        AllowMultipleConnectionsPerIp = defaults.Network.AllowMultipleConnectionsPerIp;
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
        return
        [
            new SettingsOption("System", Properties.Resources.Settings_ThemeVariant_System),
            new SettingsOption("Light", Properties.Resources.Settings_ThemeVariant_Light),
            new SettingsOption("Dark", Properties.Resources.Settings_ThemeVariant_Dark)
        ];
    }

    private static IReadOnlyList<SettingsOption> CreateColorThemeOptions()
    {
        return
        [
            new SettingsOption("Indigo", Properties.Resources.Settings_ColorTheme_Indigo),
            new SettingsOption("Cobalt", Properties.Resources.Settings_ColorTheme_Cobalt),
            new SettingsOption("Mint", Properties.Resources.Settings_ColorTheme_Mint),
            new SettingsOption("Emerald", Properties.Resources.Settings_ColorTheme_Emerald),
            new SettingsOption("Rose", Properties.Resources.Settings_ColorTheme_Rose),
            new SettingsOption("Vibrant", Properties.Resources.Settings_ColorTheme_Vibrant),
            new SettingsOption("Amber", Properties.Resources.Settings_ColorTheme_Amber),
            new SettingsOption("Slate", Properties.Resources.Settings_ColorTheme_Slate),
            new SettingsOption("Solar", Properties.Resources.Settings_ColorTheme_Solar)
        ];
    }

    private static IReadOnlyList<SettingsOption> CreateBackgroundStyleOptions()
    {
        return
        [
            new SettingsOption("GradientSoft", Properties.Resources.Settings_BackgroundStyle_GradientSoft),
            new SettingsOption("Gradient", Properties.Resources.Settings_BackgroundStyle_Gradient),
            new SettingsOption("GradientDarker", Properties.Resources.Settings_BackgroundStyle_GradientDarker),
            new SettingsOption("Flat", Properties.Resources.Settings_BackgroundStyle_Flat),
            new SettingsOption("Bubble", Properties.Resources.Settings_BackgroundStyle_Bubble)
        ];
    }

    private static IReadOnlyList<SettingsOption> CreateEncryptionModeOptions()
    {
        return
        [
            new SettingsOption("Allow", Properties.Resources.Settings_EncryptionMode_Allow),
            new SettingsOption("Require", Properties.Resources.Settings_EncryptionMode_Require),
            new SettingsOption("Refuse", Properties.Resources.Settings_EncryptionMode_Refuse)
        ];
    }

    private static IReadOnlyList<SettingsOption> CreateProxyTypeOptions()
    {
        return
        [
            new SettingsOption("None", Properties.Resources.Settings_ProxyType_None),
            new SettingsOption("Socks5", Properties.Resources.Settings_ProxyType_Socks5),
            new SettingsOption("Http", Properties.Resources.Settings_ProxyType_Http)
        ];
    }

    private async Task BrowseBlocklistAsync()
    {
        var storageProvider = _topLevelService.GetStorageProvider();

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Properties.Resources.Settings_BlocklistPicker_Title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Properties.Resources.Settings_BlocklistPicker_Filter)
                {
                    Patterns = ["*.txt", "*.p2p", "*.dat", "*.gz"]
                },
                new FilePickerFileType(Properties.Resources.Settings_BlocklistPicker_AllFiles)
                {
                    Patterns = ["*"]
                }
            ]
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
            FileTypeFilter =
            [
                new FilePickerFileType(Properties.Resources.Settings_GeoIpPicker_Filter)
                {
                    Patterns = ["*.mmdb", "*.dat", "*.csv"]
                },
                new FilePickerFileType(Properties.Resources.Settings_BlocklistPicker_AllFiles)
                {
                    Patterns = ["*"]
                }
            ]
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
                    Patterns = ["*.exe", "*.bat", "*.cmd"]
                },
                new FilePickerFileType(Properties.Resources.Settings_BlocklistPicker_AllFiles)
                {
                    Patterns = ["*"]
                }
            }
            :
            [
                new FilePickerFileType(Properties.Resources.Settings_BlocklistPicker_AllFiles)
                {
                    Patterns = ["*"]
                }
            ];

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
                    Patterns = ["*.exe", "*.bat", "*.cmd", "*.ps1"]
                },
                new FilePickerFileType(Properties.Resources.Settings_BlocklistPicker_AllFiles)
                {
                    Patterns = ["*"]
                }
            }
            :
            [
                new FilePickerFileType(Properties.Resources.Settings_CompletionActionPickerFilter)
                {
                    Patterns = ["*.sh", "*"]
                }
            ];

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
