using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Core.Config;
using Peerfluence.Core.Messaging;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.ViewModels;
using Peerfluence.Views;
using SukiUI.Controls;

namespace Peerfluence.HeadlessTests;

public class SettingsViewTests
{
    private static (SettingsView View, SettingsViewModel Vm) CreateView()
    {
        var vm = TestHelpers.CreateSettingsViewModel();
        var view = new SettingsView { DataContext = vm };

        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.ApplyTemplate();
        window.Presenter!.ApplyTemplate();

        return (view, vm);
    }

    [AvaloniaFact]
    public void View_CanBeCreated()
    {
        var (view, _) = CreateView();
        Assert.NotNull(view);
    }

    [AvaloniaFact]
    public void ToggleSwitches_ExistForFeatureFlags()
    {
        var (view, _) = CreateView();

        var toggles = view.GetLogicalDescendants().OfType<ToggleSwitch>().ToList();
        // EnableDht, EnableNatPmp, EnableUpnp, EnableSessionPersistence, EnableQueueManagement,
        // EnableBlocklist, EnableGeoIp, CheckForUpdatesOnStartup, ProxyPeers, ProxyTrackers
        Assert.True(toggles.Count >= 8, $"Expected at least 8 toggles, found {toggles.Count}");
    }

    [AvaloniaFact]
    public void ComboBoxes_ExistForSelections()
    {
        var (view, _) = CreateView();

        var combos = view.GetLogicalDescendants().OfType<ComboBox>().ToList();
        // ThemeVariant, ColorTheme, BackgroundStyle, Language, EncryptionMode, ProxyType
        Assert.True(combos.Count >= 6, $"Expected at least 6 ComboBoxes, found {combos.Count}");
    }

    [AvaloniaFact]
    public void TextBoxes_ExistForPaths()
    {
        var (view, _) = CreateView();

        var textBoxes = view.GetLogicalDescendants().OfType<TextBox>().ToList();
        // DownloadPath, SessionPath, BlocklistPath, GeoIpPath, MediaPlayerPath,
        // UpdateUrl, ProxyHost, ProxyPort, ProxyUsername, ProxyPassword
        Assert.True(textBoxes.Count >= 8, $"Expected at least 8 TextBoxes, found {textBoxes.Count}");
    }

    [AvaloniaFact]
    public void UpdateButton_NotVisibleByDefault()
    {
        var (_, vm) = CreateView();
        Assert.False(vm.IsUpdateAvailable);
    }

    [AvaloniaFact]
    public void SettingsTabs_AreUsedToGroupSections()
    {
        var (view, _) = CreateView();

        var tabs = view.FindControl<TabControl>("SettingsTabs");
        Assert.NotNull(tabs);
        Assert.True(tabs.Items.Cast<object>().Count() >= 5);
    }

    [AvaloniaFact]
    public void UpdateButton_VisibleWhenUpdateAvailable()
    {
        var (view, vm) = CreateView();
        vm.IsUpdateAvailable = true;
        var applyButton = view.GetLogicalDescendants()
            .OfType<Button>()
            .First(button => ReferenceEquals(button.Command, vm.ApplyUpdateAndRestartCommand));

        Assert.True(vm.IsUpdateAvailable);
        Assert.True(applyButton.IsVisible);
    }

    [AvaloniaFact]
    public void QueueManagement_CanBeToggled()
    {
        var (_, vm) = CreateView();

        vm.EnableQueueManagement = false;
        Assert.False(vm.EnableQueueManagement);

        vm.EnableQueueManagement = true;
        Assert.True(vm.EnableQueueManagement);
    }

    [AvaloniaFact]
    public void DownloadPath_CanBeSet()
    {
        var (_, vm) = CreateView();

        vm.DownloadPath = @"C:\test\downloads";
        Assert.Equal(@"C:\test\downloads", vm.DownloadPath);
    }

    [AvaloniaFact]
    public void ResetButton_CommandRestoresDefaultUpdateUrl()
    {
        var (view, vm) = CreateView();
        vm.UpdateUrl = "https://changed.example/feed";

        var resetButton = view.GetLogicalDescendants()
            .OfType<Button>()
            .First(button => ReferenceEquals(button.Command, vm.ResetDefaultsCommand));

        resetButton.Command!.Execute(null);

        Assert.Equal(UpdateSettings.DefaultUpdateUrl, vm.UpdateUrl);
    }

    [AvaloniaFact]
    public void ApplyUpdateButton_HiddenUntilUpdateAvailable()
    {
        var (view, vm) = CreateView();
        var applyButton = view.GetLogicalDescendants()
            .OfType<Button>()
            .First(button => ReferenceEquals(button.Command, vm.ApplyUpdateAndRestartCommand));

        Assert.False(vm.IsUpdateAvailable);
        Assert.False(applyButton.IsVisible);

        vm.IsUpdateAvailable = true;

        Assert.True(applyButton.IsVisible);
    }

    [AvaloniaFact]
    public void ThereIsNoSaveButton_BecauseChangesApplyThemselves()
    {
        var (view, vm) = CreateView();

        var buttons = view.GetLogicalDescendants().OfType<Button>().ToList();
        Assert.DoesNotContain(buttons, button => ReferenceEquals(button.Command, vm.SaveCommand));
        // Reset stays: undoing everything is still worth an explicit action.
        Assert.Contains(buttons, button => ReferenceEquals(button.Command, vm.ResetDefaultsCommand));
    }

    [AvaloniaFact]
    public void TheInterfaceModeIsOfferedHere_SoAdvancedModeHasAWayBack()
    {
        var (view, vm) = CreateView();

        var modeChoices = view.GetLogicalDescendants()
            .OfType<RadioButton>()
            .Where(radio => AutomationProperties.GetAutomationId(radio)?.StartsWith("InterfaceMode", StringComparison.Ordinal) == true)
            .ToList();

        Assert.Equal(2, modeChoices.Count);

        // Checked rather than clicked. The chips carry no command any more: they bind IsChecked both
        // ways, so that choosing one through the accessibility API - which fills the dot without
        // raising a click - switches the mode as surely as the mouse does.
        var simple = modeChoices.Single(radio =>
            AutomationProperties.GetAutomationId(radio) == "InterfaceModeSimpleRadioButton");
        simple.IsChecked = true;

        Assert.True(vm.IsSimpleMode);
    }

    [AvaloniaFact]
    public void SimpleMode_ShowsOnlyTheTabsItNeeds()
    {
        var interfaceModeService = Substitute.For<Peerfluence.Core.Services.IInterfaceModeService>();
        interfaceModeService.IsSimple.Returns(true);
        var vm = TestHelpers.CreateSettingsViewModel(interfaceModeService);
        var view = new SettingsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.ApplyTemplate();
        window.Presenter!.ApplyTemplate();

        var tabs = view.FindControl<TabControl>("SettingsTabs")!;
        var visible = tabs.Items.Cast<TabItem>().Where(tab => tab.IsVisible).ToList();

        // Appearance and Storage: where downloads go, and how the app looks.
        Assert.Equal(2, visible.Count);
    }

    /// <summary>
    /// The preset buttons carry the endpoint templates as literal parameters, so this is the only
    /// place that proves they are wired to something real rather than to an empty string.
    /// </summary>
    [AvaloniaFact]
    public void TheIndexerPresetButtons_FillInTheEndpointTheyName()
    {
        var (view, vm) = CreateView();

        var buttons = view.GetLogicalDescendants().OfType<Button>()
            .Where(button => button.Command == vm.UseIndexerPresetCommand)
            .ToList();

        Assert.Equal(2, buttons.Count);
        Assert.Contains(buttons, button => Equals(button.CommandParameter, SearchSettings.ProwlarrTemplate));
        Assert.Contains(buttons, button => Equals(button.CommandParameter, SearchSettings.JackettTemplate));

        var prowlarr = buttons.Single(button => Equals(button.CommandParameter, SearchSettings.ProwlarrTemplate));
        prowlarr.Command!.Execute(prowlarr.CommandParameter);

        Assert.Equal(SearchSettings.ProwlarrTemplate, vm.TorznabUrl);
    }

    /// <summary>
    /// Arriving from the Find torrents screen has to land on Search, not on whichever tab happened
    /// to be open. Bound to the tab rather than to an index, so this survives tabs being reordered
    /// or hidden by the current mode - which is exactly what an index would not.
    /// </summary>
    [AvaloniaFact]
    public void ArrivingFromTheSearchScreen_SelectsTheSearchTab()
    {
        var (view, _) = CreateView();
        var tabs = view.FindControl<TabControl>("SettingsTabs")!;
        Assert.NotEqual(Peerfluence.Properties.Resources.Settings_Search, (tabs.SelectedItem as TabItem)?.Header);

        WeakReferenceMessenger.Default.Send(new ShowSearchSettingsMessage());

        Assert.Equal(Peerfluence.Properties.Resources.Settings_Search, (tabs.SelectedItem as TabItem)?.Header);
    }

    [AvaloniaFact]
    public void TheSearchTab_IsThere_InAdvancedMode()
    {
        var (view, _) = CreateView();

        var tabs = view.FindControl<TabControl>("SettingsTabs")!;
        var headers = tabs.Items.Cast<TabItem>().Where(tab => tab.IsVisible).Select(tab => tab.Header).ToList();

        Assert.Contains(Peerfluence.Properties.Resources.Settings_Search, headers);
    }

    [AvaloniaFact]
    public void StatusInfoBar_ReflectsStatusMessage()
    {
        var (view, vm) = CreateView();
        var infoBar = view.FindControl<InfoBar>("SettingsStatusInfoBar");

        Assert.NotNull(infoBar);
        Assert.False(infoBar.IsVisible);

        vm.StatusMessage = "Saved";

        Assert.True(infoBar.IsVisible);
        Assert.Equal("Saved", infoBar.Message);
    }
}
