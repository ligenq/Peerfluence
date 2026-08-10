using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.ViewModels;
using Peerfluence.Views;
using SukiUI.Controls;

namespace Peerfluence.HeadlessTests;

public sealed class MainWindowViewTests
{
    [AvaloniaFact]
    public void Window_CreatesToastAndDialogHosts_WhenConstructedWithViewModel()
    {
        var downloadsVm = TestHelpers.CreateDownloadsViewModel();
        var settingsVm = TestHelpers.CreateSettingsViewModel();
        var aboutVm = new AboutViewModel(Substitute.For<ILogger<AboutViewModel>>());
        var mainVm = TestHelpers.CreateMainWindowViewModel(downloadsVm, settingsVm, aboutVm);

        var view = new MainWindowView(mainVm);

        Assert.Equal(2, view.Hosts.Count);
    }

    [AvaloniaFact]
    public void Window_GivesItsContentAnOverlayLayer()
    {
        // A popup's light dismiss installs itself in the OverlayLayer, which lives in a
        // VisualLayerManager. SukiWindow's template has none, and without one every context menu in
        // the app stays open until the window is deactivated - clicks inside it do nothing.
        var downloadsVm = TestHelpers.CreateDownloadsViewModel();
        var settingsVm = TestHelpers.CreateSettingsViewModel();
        var aboutVm = new AboutViewModel(Substitute.For<ILogger<AboutViewModel>>());
        var mainVm = TestHelpers.CreateMainWindowViewModel(downloadsVm, settingsVm, aboutVm);

        // Shown rather than templated: SukiWindow builds its content tree on being shown, and the
        // layer has to be reachable from the content, which is what a context menu asks from.
        var view = new MainWindowView(mainVm);
        view.Show();

        try
        {
            var content = view.GetVisualDescendants().OfType<SukiSideMenu>().FirstOrDefault();
            Assert.NotNull(content);

            // The manager, not the layer it builds: the layer itself is only realized once
            // something renders, which headless does not do.
            Assert.Contains(content.GetVisualAncestors(), ancestor => ancestor is VisualLayerManager);
        }
        finally
        {
            view.Close();
        }
    }
}
