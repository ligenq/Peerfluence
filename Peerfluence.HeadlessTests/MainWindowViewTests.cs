using Microsoft.Extensions.Logging;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.ViewModels;
using Peerfluence.Views;

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

}
