using Microsoft.Extensions.DependencyInjection;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.ViewModels;
using Peerfluence.Views;

namespace Peerfluence.HeadlessTests;

public class NavigationTests
{
    [AvaloniaFact]
    public void DefaultPage_IsDownloadsViewModel()
    {
        var vm = TestHelpers.CreateMainWindowViewModel();
        Assert.IsType<DownloadsViewModel>(vm.CurrentPage);
    }

    [AvaloniaFact]
    public void SelectingSettings_SwitchesCurrentPage()
    {
        var vm = TestHelpers.CreateMainWindowViewModel();

        vm.SelectedNavigationItem = vm.NavigationItems[1];

        Assert.IsType<SettingsViewModel>(vm.CurrentPage);
    }

    [AvaloniaFact]
    public void ShowAbout_SetsCurrentPageToAbout_ClearsNavSelection()
    {
        var vm = TestHelpers.CreateMainWindowViewModel();

        vm.ShowAboutCommand.Execute(null);

        Assert.IsType<AboutViewModel>(vm.CurrentPage);
        Assert.Null(vm.SelectedNavigationItem);
    }

    [AvaloniaFact]
    public void SelectingDownloadsAfterAbout_RestoresDownloadsPage()
    {
        var vm = TestHelpers.CreateMainWindowViewModel();

        vm.ShowAboutCommand.Execute(null);
        vm.SelectedNavigationItem = vm.NavigationItems[0];

        Assert.IsType<DownloadsViewModel>(vm.CurrentPage);
    }

    [AvaloniaFact]
    public void MainWindow_ContentControl_ResolvesView()
    {
        var services = new ServiceCollection();
        services.AddTransient<DownloadsView>();
        using var sp = services.BuildServiceProvider();
        ViewLocator.Services = sp;

        try
        {
            var vm = TestHelpers.CreateMainWindowViewModel();
            var viewLocator = new ViewLocator();

            // The ContentControl uses ViewLocator to resolve the view
            var control = viewLocator.Build(vm.CurrentPage);

            Assert.IsType<DownloadsView>(control);
        }
        finally
        {
            ViewLocator.Services = null;
        }
    }

    [AvaloniaFact]
    public void NavigationItems_HasTwoEntries()
    {
        var vm = TestHelpers.CreateMainWindowViewModel();
        Assert.Equal(2, vm.NavigationItems.Count);
    }
}
