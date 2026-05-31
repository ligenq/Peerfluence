using Avalonia.Controls;
using Avalonia.LogicalTree;
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

        Assert.Equal(string.Empty, vm.UpdateUrl);
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
