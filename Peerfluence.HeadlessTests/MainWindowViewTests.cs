using Peerfluence.Theme.Controls;
using Avalonia.LogicalTree;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.ViewModels;
using Peerfluence.Views;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Peerfluence.HeadlessTests;

public sealed class MainWindowViewTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task About_HighlightsTheCurrentDestinationAndClearsWhenNavigatingAway(bool dark)
    {
        var mainVm = TestHelpers.CreateMainWindowViewModel();
        await mainVm.ChooseAdvancedModeCommand.ExecuteAsync(null);
        var view = new MainWindowView(mainVm)
        {
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        view.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            view.UpdateLayout();
            var about = view.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetAutomationId(button) == "ShowAboutButton");
            var presenter = about.GetVisualDescendants().OfType<ContentPresenter>().First();
            Assert.False(mainVm.IsAboutSelected);
            mainVm.ShowAboutCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            view.UpdateLayout();
            Assert.True(mainVm.IsAboutSelected);
            Assert.Null(mainVm.SelectedNavigationItem);
            var selectedBackground = Assert.IsType<SolidColorBrush>(view.FindResource(view.ActualThemeVariant, "PfAccentSubtle")).Color;
            var selectedForeground = Assert.IsType<SolidColorBrush>(view.FindResource(view.ActualThemeVariant, "PfAccent")).Color;
            Assert.Equal(selectedBackground, Assert.IsType<SolidColorBrush>(presenter.Background).Color);
            Assert.Equal(selectedForeground, Assert.IsType<SolidColorBrush>(about.GetVisualDescendants().OfType<TextBlock>().Single().Foreground).Color);
            var point = about.TranslatePoint(new Point(10, 10), view)!.Value;
            view.MouseMove(point);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(selectedBackground, Assert.IsType<SolidColorBrush>(presenter.Background).Color);
            mainVm.SelectedNavigationItem = mainVm.NavigationItems.First();
            Dispatcher.UIThread.RunJobs();
            Assert.False(mainVm.IsAboutSelected);
            Assert.DoesNotContain("selected", about.Classes);
            Assert.NotEqual(selectedBackground, Assert.IsType<SolidColorBrush>(presenter.Background).Color);
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void Window_CreatesToastAndDialogHosts_WhenConstructedWithViewModel()
    {
        var downloadsVm = TestHelpers.CreateDownloadsViewModel();
        var settingsVm = TestHelpers.CreateSettingsViewModel();
        var aboutVm = new AboutViewModel(Substitute.For<ILogger<AboutViewModel>>());
        var mainVm = TestHelpers.CreateMainWindowViewModel(downloadsVm, settingsVm, aboutVm);

        var view = new MainWindowView(mainVm);

        // The hosts are declared in markup and bound to the view model's managers, rather than
        // added in code to a collection the window type used to provide. Checking the binding as
        // well as the presence is the point: a host wired to nothing shows nothing, and that is
        // exactly the failure the previous count could not see.
        var hosts = view.GetLogicalDescendants().ToList();
        var toasts = hosts.OfType<ToastHost>().SingleOrDefault();
        var dialogs = hosts.OfType<DialogHost>().SingleOrDefault();

        Assert.NotNull(toasts);
        Assert.NotNull(dialogs);
        Assert.Same(mainVm.ToastManager, toasts.Manager);
        Assert.Same(mainVm.DialogManager, dialogs.Manager);
    }

    [AvaloniaFact]
    public void Window_GivesItsContentAnOverlayLayer()
    {
        // A popup's light dismiss installs itself in the OverlayLayer, which lives in a
        // VisualLayerManager. The window this replaced had a template with none, and without one
        // every context menu in the app stayed open until the window was deactivated - clicks
        // inside it did nothing. An ordinary Window brings one, which is what this holds it to.
        var downloadsVm = TestHelpers.CreateDownloadsViewModel();
        var settingsVm = TestHelpers.CreateSettingsViewModel();
        var aboutVm = new AboutViewModel(Substitute.For<ILogger<AboutViewModel>>());
        var mainVm = TestHelpers.CreateMainWindowViewModel(downloadsVm, settingsVm, aboutVm);

        // Shown rather than templated: the content tree is built on being shown, and the layer has
        // to be reachable from the content, which is what a context menu asks from.
        var view = new MainWindowView(mainVm);
        view.Show();

        try
        {
            var content = view.GetVisualDescendants().OfType<ListBox>().FirstOrDefault();
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

    [AvaloniaFact]
    public async Task SimpleDownloadsView_IsBuiltOnlyInSimpleMode()
    {
        // A control declared in markup is constructed whether or not it is visible, so the simple
        // screen used to be built at startup in advanced mode, where nobody sees it.
        var mainVm = TestHelpers.CreateMainWindowViewModel();
        await mainVm.ChooseAdvancedModeCommand.ExecuteAsync(null);
        var view = new MainWindowView(mainVm);
        view.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(view.GetVisualDescendants().OfType<SimpleDownloadsView>());

            await mainVm.ChooseSimpleModeCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            view.UpdateLayout();

            var simple = Assert.Single(view.GetVisualDescendants().OfType<SimpleDownloadsView>());
            Assert.Same(mainVm.DownloadsViewModel, simple.DataContext);
            Assert.Contains(
                simple.GetVisualDescendants().OfType<Button>(),
                button => AutomationProperties.GetAutomationId(button) == "SimpleDownloadsAddMagnetButton");
        }
        finally
        {
            view.Close();
        }
    }
}
