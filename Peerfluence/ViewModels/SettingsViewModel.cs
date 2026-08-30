using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
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
    private readonly ITorrentSearchService _searchService;
    private readonly ITorrentCategoryService _categoryService;
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
        IInterfaceModeService interfaceModeService,
        ITorrentSearchService searchService,
        ITorrentCategoryService categoryService)
    {
        _interfaceModeService = interfaceModeService;
        _searchService = searchService;
        _categoryService = categoryService;
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
        BrowseWatchFolderPathCommand = new AsyncRelayCommand(BrowseWatchFolderPathAsync);
        RefreshPortMappingCommand = new RelayCommand(RefreshPortMapping);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        ApplyUpdateAndRestartCommand = new RelayCommand(ApplyUpdateAndRestart);

        SetInterfaceModeCommand = new AsyncRelayCommand<InterfaceMode>(SetInterfaceModeAsync);
        UseIndexerPresetCommand = new AsyncRelayCommand<string>(UseIndexerPresetAsync);
        AddCategoryCommand = new AsyncRelayCommand(AddCategoryAsync, () => NewCategoryName.Trim().Length > 0);
        RemoveCategoryCommand = new AsyncRelayCommand<string?>(RemoveCategoryAsync);
        BrowseCategoryPathCommand = new AsyncRelayCommand(BrowseCategoryPathAsync);
        RefreshCategories();
        DetectIndexerCommand = new AsyncRelayCommand(DetectIndexerAsync);
        TestIndexerCommand = new AsyncRelayCommand(TestIndexerAsync);

        WeakReferenceMessenger.Default.Register<InterfaceModeChangedMessage>(this, (_, msg) => ApplyInterfaceMode(msg.Mode));

        // Someone arriving from the Find torrents screen is here for one thing, so open on it.
        WeakReferenceMessenger.Default.Register<ShowSearchSettingsMessage>(this, (_, _) => SelectedTabIndex = SearchTabIndex);

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
        OnPropertyChanged(nameof(SimpleModeSelected));
        OnPropertyChanged(nameof(AdvancedModeSelected));
    }

    /// <summary>
    /// Sets one of the fixed-choice settings, ignoring an empty value.
    ///
    /// <para>
    /// There is no "no theme" or "no proxy type" to choose, so an empty value is never the user
    /// picking something - it is a ComboBox reporting that it currently has nothing selected, which
    /// it does briefly whenever its items are replaced. Changing the language replaces all five of
    /// these lists to relabel them, and letting that transient nothing through meant storing a null
    /// theme and handing it straight to the theme service.
    /// </para>
    /// </summary>
    private void SetChoice(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        SetProperty(ref field, value, propertyName);
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
        nameof(SimpleModeSelected),
        nameof(AdvancedModeSelected),
        nameof(SearchStatusMessage),
        nameof(HasSearchStatusMessage),
        nameof(SelectedTabIndex),
        nameof(RemoteNeedsCredentials),
        nameof(NewCategoryName),
        nameof(NewCategoryPath),
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
        if (e.PropertyName is nameof(RemoteEnabled) or nameof(RemoteAllowRemoteConnections) or nameof(RemoteUsername))
        {
            OnPropertyChanged(nameof(RemoteNeedsCredentials));
        }

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

    /// <summary>
    /// How many connections one address may hold on a single torrent. Zero is unlimited, and is the
    /// middle ground <see cref="AllowMultipleConnectionsPerIp"/> lacked - that could only allow one
    /// per address or as many as turned up.
    /// </summary>
    public int MaxConnectionsPerIp
    {
        get;
        set => SetProperty(ref field, Math.Max(0, value));
    }

    /// <summary>
    /// A single local address every socket is bound to, or blank for all interfaces. Kept as typed
    /// rather than validated into an <see cref="System.Net.IPAddress"/> here, so a half-typed address
    /// is not rejected mid-keystroke; <see cref="IsBindAddressValid"/> reports on it instead.
    /// </summary>
    public string BindAddress
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsBindAddressValid));
            }
        }
    } = string.Empty;

    /// <summary>
    /// Whether what is typed is an address the engine can bind to. Blank is valid and means every
    /// interface. <c>0.0.0.0</c> and <c>::</c> are not: the engine rejects them outright, because
    /// "any address" is not a single-address guarantee it could keep.
    /// </summary>
    public bool IsBindAddressValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(BindAddress))
            {
                return true;
            }

            return System.Net.IPAddress.TryParse(BindAddress, out var parsed)
                && !parsed.Equals(System.Net.IPAddress.Any)
                && !parsed.Equals(System.Net.IPAddress.IPv6Any);
        }
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

    /// <summary>
    /// The two interface mode chips as a radio button sees them: reading says which dot to fill,
    /// writing is somebody choosing.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="IsSimpleMode"/>, which reports the mode in force and is assigned
    /// whenever it changes - including by the link in simple mode, which is not this screen. A
    /// property that both reported the mode and changed it would answer its own assignment.
    /// </remarks>
    public bool SimpleModeSelected
    {
        get => IsSimpleMode;
        set => ChooseWhenChecked(value, InterfaceMode.Simple);
    }

    /// <inheritdoc cref="SimpleModeSelected"/>
    public bool AdvancedModeSelected
    {
        get => IsAdvancedMode;
        set => ChooseWhenChecked(value, InterfaceMode.Advanced);
    }

    /// <summary>
    /// Switches to the mode a chip stands for, when that chip is the one being checked.
    /// </summary>
    /// <remarks>
    /// Only when checked, because choosing one member of a radio group unchecks the rest and both
    /// arrive here in no guaranteed order.
    /// </remarks>
    private void ChooseWhenChecked(bool isChecked, InterfaceMode mode)
    {
        if (isChecked && Current() != mode)
        {
            SetInterfaceModeCommand.Execute(mode);
        }

        InterfaceMode Current() => IsSimpleMode ? InterfaceMode.Simple : InterfaceMode.Advanced;
    }

    // Remote control
    public bool RemoteEnabled
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int RemotePort
    {
        get;
        set => SetProperty(ref field, Math.Clamp(value, 1, 65535));
    } = RemoteSettings.DefaultPort;

    public bool RemoteAllowRemoteConnections
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string RemoteUsername
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string RemotePassword
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>
    /// Whether the combination chosen would be refused at startup. Said here rather than discovered
    /// later in a log nobody reads: opening the port to the network without a password would hand
    /// anyone who can reach it the ability to add and delete downloads.
    /// </summary>
    public bool RemoteNeedsCredentials =>
        RemoteEnabled && RemoteAllowRemoteConnections && string.IsNullOrWhiteSpace(RemoteUsername);

    // Categories
    public ObservableCollection<TorrentCategory> CategoryList { get; } = new();

    public string NewCategoryName
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AddCategoryCommand.NotifyCanExecuteChanged();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Where the new category will save to. Optional: a category with no path of its own is still
    /// useful for grouping, it just leaves downloads where they would have gone anyway.
    /// </summary>
    public string NewCategoryPath
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public IAsyncRelayCommand AddCategoryCommand { get; private set; } = null!;

    public IAsyncRelayCommand<string?> RemoveCategoryCommand { get; private set; } = null!;

    public IAsyncRelayCommand BrowseCategoryPathCommand { get; private set; } = null!;

    private async Task AddCategoryAsync()
    {
        await _categoryService.AddAsync(NewCategoryName, NewCategoryPath).ConfigureAwait(true);

        NewCategoryName = string.Empty;
        NewCategoryPath = string.Empty;
        RefreshCategories();
    }

    private async Task RemoveCategoryAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await _categoryService.RemoveAsync(name).ConfigureAwait(true);
        RefreshCategories();
    }

    private async Task BrowseCategoryPathAsync()
    {
        var folders = await _topLevelService.GetStorageProvider().OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Properties.Resources.Settings_DownloadFolderPicker_Title,
            AllowMultiple = false
        });

        if (folders.Count > 0 && folders[0] is { } folder)
        {
            NewCategoryPath = folder.Path.LocalPath;
        }
    }

    private void RefreshCategories()
    {
        CategoryList.Clear();
        foreach (var category in _categoryService.Categories)
        {
            CategoryList.Add(category);
        }
    }

    // Search
    public string TorznabUrl
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string TorznabApiKey
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>
    /// What Detect or Test last found. Not a setting, so it neither saves nor triggers one.
    /// </summary>
    public string SearchStatusMessage
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasSearchStatusMessage));
            }
        }
    } = string.Empty;

    public bool HasSearchStatusMessage => !string.IsNullOrWhiteSpace(SearchStatusMessage);

    /// <summary>
    /// Where Search sits among the tabs. A fixed number is safe because tabs are hidden rather than
    /// removed when the mode changes, so the positions do not shift - and if anyone reorders them,
    /// the headless test that checks the selected tab's header says so.
    /// </summary>
    public const int SearchTabIndex = 6;

    /// <summary>
    /// Which tab is showing. Settable so that arriving here from somewhere that knows what it came
    /// for lands on the right one.
    /// </summary>
    public int SelectedTabIndex
    {
        get;
        set => SetProperty(ref field, value);
    }

    public IAsyncRelayCommand<string> UseIndexerPresetCommand { get; private set; } = null!;

    public IAsyncRelayCommand DetectIndexerCommand { get; private set; } = null!;

    public IAsyncRelayCommand TestIndexerCommand { get; private set; } = null!;

    /// <summary>
    /// Fills in the endpoint for one of the two indexer managers people actually run, so the only
    /// thing left to supply is the key. Nothing here names a torrent index: which indexes exist is
    /// configured in Prowlarr or Jackett, by the person running it.
    /// </summary>
    private async Task UseIndexerPresetAsync(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return;
        }

        TorznabUrl = template;

        // Prowlarr's address is not usable as handed over: the number in it names one indexer, and
        // only the user knows which. Saying so beats letting the test fail and leaving them to
        // wonder which part was wrong.
        SearchStatusMessage = template == SearchSettings.ProwlarrTemplate
            ? Properties.Resources.Settings_Search_ProwlarrNeedsIndexerId
            : Properties.Resources.Settings_Search_PresetApplied;

        // Checked straight away rather than left for the user to discover on the search screen. A
        // preset fills in an address for software they may not have installed, and finding that out
        // here - next to the buttons that can fix it - is the whole point of pressing one.
        //
        // Awaited rather than started and forgotten: a fire-and-forget async call from a synchronous
        // command puts any failure inside it on a thread with nobody listening.
        await TestIndexerAsync().ConfigureAwait(true);
    }

    private async Task DetectIndexerAsync()
    {
        SearchStatusMessage = Properties.Resources.Settings_Search_Detecting;

        var found = await _searchService.DetectLocalEndpointAsync().ConfigureAwait(true);
        if (found == null)
        {
            SearchStatusMessage = Properties.Resources.Settings_Search_NotDetected;
            return;
        }

        TorznabUrl = found;
        SearchStatusMessage = Properties.Resources.Settings_Search_Detected;
    }

    /// <summary>
    /// Checks the address currently in the box. Nothing has to be applied first: a change is in
    /// force in the settings object the moment it is made, and that object is where the service
    /// reads the endpoint from. Only the write to disk is delayed.
    /// </summary>
    private async Task TestIndexerAsync()
    {
        SearchStatusMessage = Properties.Resources.Settings_Search_Testing;

        var response = await _searchService.TestAsync().ConfigureAwait(true);
        SearchStatusMessage = DescribeTest(response);
    }

    /// <summary>
    /// The same distinctions the Find torrents screen makes, said here where they can be acted on.
    /// "Nothing is running there" and "it refused your key" send the user to different places, and
    /// the raw socket message sends them nowhere.
    /// </summary>
    private static string DescribeTest(TorrentSearchResponse response)
    {
        return response.Failure switch
        {
            SearchFailure.None => Properties.Resources.Settings_Search_TestPassed,
            SearchFailure.NotConfigured => Properties.Resources.Settings_Search_TestNotConfigured,
            SearchFailure.Unreachable => string.Format(
                Properties.Resources.Settings_Search_TestUnreachable,
                response.FailureDetail ?? string.Empty),
            SearchFailure.Rejected => Properties.Resources.Settings_Search_TestRejected,
            SearchFailure.NotTorznab => Properties.Resources.Settings_Search_TestNotTorznab,
            _ => string.Format(
                Properties.Resources.Settings_Search_TestFailed,
                response.FailureDetail ?? string.Empty)
        };
    }

    public int ListeningPort
    {
        get;
        set => SetProperty(ref field, Math.Clamp(value, 1, 65535));
    } = NetworkSettings.DefaultListeningPort;

    /// <summary>
    /// The download limit in kibibytes per second, which is the unit every other client uses and the
    /// one people think in. Zero is unlimited. Stored in bytes, because that is what the engine takes.
    /// </summary>
    public long MaxDownloadSpeedKibibytesPerSecond
    {
        get;
        set => SetProperty(ref field, Math.Max(0, value));
    }

    /// <summary>The upload limit, on the same terms.</summary>
    public long MaxUploadSpeedKibibytesPerSecond
    {
        get;
        set => SetProperty(ref field, Math.Max(0, value));
    }

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
        set => SetChoice(ref field, value);
    } = "System";

    public string SelectedColorTheme
    {
        get;
        set => SetChoice(ref field, value);
    } = "Indigo";

    public string SelectedBackgroundStyle
    {
        get;
        set => SetChoice(ref field, value);
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

    // Seeding goals. What a torrent is told when nobody tells it anything.
    public bool LimitSeedingRatio
    {
        get;
        set => SetProperty(ref field, value);
    }

    public float SeedingRatioLimit
    {
        get;
        set => SetProperty(ref field, Math.Max(0f, value));
    } = 2.0f;

    public bool LimitSeedingTime
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int SeedingTimeLimitMinutes
    {
        get;
        set => SetProperty(ref field, Math.Max(0, value));
    } = 1440;

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

    /// <summary>
    /// Where the completion action runs. No longer shown on the settings screen.
    /// </summary>
    /// <remarks>
    /// The behaviour is worth having - a script that unpacks or moves what was downloaded works in
    /// relative paths, and without this it works relative to wherever Peerfluence was started from -
    /// but the field was not. Its only sensible values were the default and empty, a directory that
    /// does not exist stops the action running at all rather than falling back, and it was the
    /// fourth of five boxes in the densest card on the screen.
    ///
    /// <para>
    /// Kept rather than deleted because of Reset defaults, which works by assigning every property
    /// on this screen from a fresh <c>AppSettings</c>. Without it, resetting would put everything
    /// back except this, and a hand-edited working directory would outlive the reset that was meant
    /// to clear it. Saving would preserve the value either way: <c>ApplyToSettings</c> mutates the
    /// stored settings in place, so what nobody assigns is left alone.
    /// </para>
    /// </remarks>
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
        set => SetChoice(ref field, value);
    } = "Allow";

    // Proxy
    public string SelectedProxyType
    {
        get;
        set => SetChoice(ref field, value);
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

    public IAsyncRelayCommand BrowseWatchFolderPathCommand { get; }

    // The saved query that runs on its own.
    public bool AutoSearchEnabled
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string AutoSearchQuery
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public int AutoSearchIntervalMinutes
    {
        get;
        set => SetProperty(ref field, Math.Max(Peerfluence.Core.Services.AutoSearch.MinimumIntervalMinutes, value));
    } = 60;

    public string AutoSearchCategory
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    // The scheduled window: different speed limits while somebody is using the connection.
    public bool ScheduleEnabled
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string ScheduleFrom
    {
        get;
        set => SetProperty(ref field, value);
    } = "08:00";

    public string ScheduleTo
    {
        get;
        set => SetProperty(ref field, value);
    } = "18:00";

    public long ScheduleDownloadKibibytesPerSecond
    {
        get;
        set => SetProperty(ref field, Math.Max(0, value));
    }

    public long ScheduleUploadKibibytesPerSecond
    {
        get;
        set => SetProperty(ref field, Math.Max(0, value));
    }

    /// <summary>
    /// The days the window runs on, named by the culture rather than by the resource files.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<ScheduleDayViewModel> ScheduleDays { get; } =
        BuildScheduleDays();

    private static System.Collections.Generic.IReadOnlyList<ScheduleDayViewModel> BuildScheduleDays()
    {
        var format = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat;

        // Starting on the culture's own first day, so a week reads the way the reader expects.
        var first = (int)format.FirstDayOfWeek;
        var days = new ScheduleDayViewModel[7];
        for (int i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)((first + i) % 7);
            days[i] = new ScheduleDayViewModel(day, format.GetAbbreviatedDayName(day), true);
        }

        return days;
    }

    // The watched folder: torrent files dropped here are added without a dialog.
    public bool WatchFolderEnabled
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string WatchFolderPath
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

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
        MaxConnectionsPerIp = settings.Network.MaxConnectionsPerIp;
        BindAddress = settings.Network.BindAddress;
        EnableNatPmp = settings.Network.EnableNatPmp;
        EnableUpnp = settings.Network.EnableUpnp;
        UseAutomaticListeningPort = settings.Network.UseAutomaticListeningPort;
        ListeningPort = settings.Network.ListeningPort;
        MaxDownloadSpeedKibibytesPerSecond = ToKibibytes(settings.Network.MaxDownloadSpeedBytesPerSecond);
        MaxUploadSpeedKibibytesPerSecond = ToKibibytes(settings.Network.MaxUploadSpeedBytesPerSecond);
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
        AutoSearchEnabled = settings.AutoSearch.Enabled;
        AutoSearchQuery = settings.AutoSearch.Query;
        AutoSearchIntervalMinutes = settings.AutoSearch.IntervalMinutes;
        AutoSearchCategory = settings.AutoSearch.Category;
        ScheduleEnabled = settings.Schedule.Enabled;
        ScheduleFrom = settings.Schedule.From;
        ScheduleTo = settings.Schedule.To;
        ScheduleDownloadKibibytesPerSecond = ToKibibytes(settings.Schedule.DownloadLimitBytesPerSecond);
        ScheduleUploadKibibytesPerSecond = ToKibibytes(settings.Schedule.UploadLimitBytesPerSecond);
        ReadScheduleDays(settings.Schedule);
        WatchFolderEnabled = settings.WatchFolder.Enabled;
        WatchFolderPath = settings.WatchFolder.Path;
        LimitSeedingRatio = settings.Seeding.LimitRatio;
        SeedingRatioLimit = settings.Seeding.RatioLimit;
        LimitSeedingTime = settings.Seeding.LimitSeedTime;
        SeedingTimeLimitMinutes = settings.Seeding.SeedTimeLimitMinutes;

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

        // Search
        RemoteEnabled = settings.Remote.Enabled;
        RemotePort = settings.Remote.Port;
        RemoteAllowRemoteConnections = settings.Remote.AllowRemoteConnections;
        RemoteUsername = settings.Remote.Username;
        RemotePassword = settings.Remote.Password;
        TorznabUrl = settings.Search.TorznabUrl;
        TorznabApiKey = settings.Search.ApiKey;
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
            settings.Network.MaxConnectionsPerIp = MaxConnectionsPerIp;

            // Only stored once it parses. A half-typed address saved as it stands would be read back
            // as "no bind address" on the next start, which is the opposite of what a kill switch is
            // for - it would silently go back to leaving by the default route.
            settings.Network.BindAddress = IsBindAddressValid ? BindAddress.Trim() : settings.Network.BindAddress;
            settings.Network.EnableNatPmp = EnableNatPmp;
            settings.Network.EnableUpnp = EnableUpnp;
            settings.Network.UseAutomaticListeningPort = UseAutomaticListeningPort;
            settings.Network.ListeningPort = ListeningPort;
            settings.Network.MaxDownloadSpeedBytesPerSecond = ToBytes(MaxDownloadSpeedKibibytesPerSecond);
            settings.Network.MaxUploadSpeedBytesPerSecond = ToBytes(MaxUploadSpeedKibibytesPerSecond);
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
            settings.AutoSearch.Enabled = AutoSearchEnabled;
            settings.AutoSearch.Query = AutoSearchQuery;
            settings.AutoSearch.IntervalMinutes = AutoSearchIntervalMinutes;
            settings.AutoSearch.Category = AutoSearchCategory;
            settings.Schedule.Enabled = ScheduleEnabled;
            settings.Schedule.From = ScheduleFrom;
            settings.Schedule.To = ScheduleTo;
            settings.Schedule.DownloadLimitBytesPerSecond = ToBytes(ScheduleDownloadKibibytesPerSecond);
            settings.Schedule.UploadLimitBytesPerSecond = ToBytes(ScheduleUploadKibibytesPerSecond);
            WriteScheduleDays(settings.Schedule);
            settings.WatchFolder.Enabled = WatchFolderEnabled;
            settings.WatchFolder.Path = WatchFolderPath;
            settings.Seeding.LimitRatio = LimitSeedingRatio;
            settings.Seeding.RatioLimit = SeedingRatioLimit;
            settings.Seeding.LimitSeedTime = LimitSeedingTime;
            settings.Seeding.SeedTimeLimitMinutes = SeedingTimeLimitMinutes;

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

            // Search
            settings.Remote.Enabled = RemoteEnabled;
            settings.Remote.Port = RemotePort;
            settings.Remote.AllowRemoteConnections = RemoteAllowRemoteConnections;
            settings.Remote.Username = RemoteUsername.Trim();
            settings.Remote.Password = RemotePassword;
            settings.Search.TorznabUrl = TorznabUrl;
            settings.Search.ApiKey = TorznabApiKey;
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
    private const long BytesPerKibibyte = 1024;

    private static long ToKibibytes(long bytes) => bytes / BytesPerKibibyte;

    private static long ToBytes(long kibibytes) => kibibytes * BytesPerKibibyte;

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

        // Unconditional, unlike the three above: pushing two numbers at a running engine costs
        // nothing, and there is no side effect to avoid repeating.
        _engineService.ApplySpeedLimits();
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
        MaxConnectionsPerIp = defaults.Network.MaxConnectionsPerIp;
        BindAddress = defaults.Network.BindAddress;
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
        AutoSearchEnabled = defaults.AutoSearch.Enabled;
        AutoSearchQuery = defaults.AutoSearch.Query;
        AutoSearchIntervalMinutes = defaults.AutoSearch.IntervalMinutes;
        AutoSearchCategory = defaults.AutoSearch.Category;
        ScheduleEnabled = defaults.Schedule.Enabled;
        ScheduleFrom = defaults.Schedule.From;
        ScheduleTo = defaults.Schedule.To;
        ScheduleDownloadKibibytesPerSecond = ToKibibytes(defaults.Schedule.DownloadLimitBytesPerSecond);
        ScheduleUploadKibibytesPerSecond = ToKibibytes(defaults.Schedule.UploadLimitBytesPerSecond);
        ReadScheduleDays(defaults.Schedule);
        WatchFolderEnabled = defaults.WatchFolder.Enabled;
        WatchFolderPath = defaults.WatchFolder.Path;
        LimitSeedingRatio = defaults.Seeding.LimitRatio;
        SeedingRatioLimit = defaults.Seeding.RatioLimit;
        LimitSeedingTime = defaults.Seeding.LimitSeedTime;
        SeedingTimeLimitMinutes = defaults.Seeding.SeedTimeLimitMinutes;
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

        var file = files.Count > 0 ? files[0] : null;
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

        var file = files.Count > 0 ? files[0] : null;
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

        var file = files.Count > 0 ? files[0] : null;
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

        var file = files.Count > 0 ? files[0] : null;
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

        var folder = folders.Count > 0 ? folders[0] : null;
        if (folder != null)
        {
            DownloadPath = folder.Path.LocalPath;
        }
    }

    private void ReadScheduleDays(Peerfluence.Core.Config.ScheduleSettings schedule)
    {
        foreach (var day in ScheduleDays)
        {
            day.IsSelected = Peerfluence.Core.Services.BandwidthSchedule.IsSelected(schedule, day.Day);
        }
    }

    private void WriteScheduleDays(Peerfluence.Core.Config.ScheduleSettings schedule)
    {
        foreach (var day in ScheduleDays)
        {
            switch (day.Day)
            {
                case DayOfWeek.Monday: schedule.Monday = day.IsSelected; break;
                case DayOfWeek.Tuesday: schedule.Tuesday = day.IsSelected; break;
                case DayOfWeek.Wednesday: schedule.Wednesday = day.IsSelected; break;
                case DayOfWeek.Thursday: schedule.Thursday = day.IsSelected; break;
                case DayOfWeek.Friday: schedule.Friday = day.IsSelected; break;
                case DayOfWeek.Saturday: schedule.Saturday = day.IsSelected; break;
                default: schedule.Sunday = day.IsSelected; break;
            }
        }
    }

    private async Task BrowseWatchFolderPathAsync()
    {
        var storageProvider = _topLevelService.GetStorageProvider();

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Properties.Resources.Settings_WatchFolderPicker_Title,
            AllowMultiple = false
        });

        var folder = folders.Count > 0 ? folders[0] : null;
        if (folder != null)
        {
            WatchFolderPath = folder.Path.LocalPath;
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

        var folder = folders.Count > 0 ? folders[0] : null;
        if (folder != null)
        {
            SessionPath = folder.Path.LocalPath;
        }
    }
}

public sealed record SettingsOption(string Value, string DisplayName);
