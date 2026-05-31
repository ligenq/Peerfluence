using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Peerfluence.ViewModels;
using Peerfluence.Views;
using Microsoft.Extensions.Logging;
using Peerfluence.HeadlessTests.XUnit;

namespace Peerfluence.HeadlessTests;

public class ViewLocatorTests : IDisposable
{
    private readonly ViewLocator _sut = new();
    private readonly ServiceProvider _serviceProvider;

    public ViewLocatorTests()
    {
        var services = new ServiceCollection();
        services.AddTransient<MainWindowView>();
        services.AddTransient<DownloadsView>();
        services.AddTransient<DetailsView>();
        services.AddTransient<SettingsView>();
        services.AddTransient<AboutView>();
        _serviceProvider = services.BuildServiceProvider();
        ViewLocator.Services = _serviceProvider;
    }

    public void Dispose()
    {
        ViewLocator.Services = null;
        _serviceProvider.Dispose();
    }

    [Fact]
    public void Match_ViewModelBase_ReturnsTrue()
    {
        var vm = TestHelpers.CreateSettingsViewModel();
        Assert.True(_sut.Match(vm));
    }

    [Fact]
    public void Match_NonViewModel_ReturnsFalse()
    {
        Assert.False(_sut.Match("not a view model"));
    }

    [Fact]
    public void Match_Null_ReturnsFalse()
    {
        Assert.False(_sut.Match(null));
    }

    [AvaloniaFact]
    public void Build_DownloadsViewModel_ReturnsDownloadsView()
    {
        var vm = TestHelpers.CreateDownloadsViewModel();
        var control = _sut.Build(vm);

        Assert.IsType<DownloadsView>(control);
        Assert.Equal(vm, control!.DataContext);
    }

    [AvaloniaFact]
    public void Build_SettingsViewModel_ReturnsSettingsView()
    {
        var vm = TestHelpers.CreateSettingsViewModel();
        var control = _sut.Build(vm);

        Assert.IsType<SettingsView>(control);
        Assert.Equal(vm, control!.DataContext);
    }

    [AvaloniaFact]
    public void Build_DetailsViewModel_ReturnsDetailsView()
    {
        var vm = TestHelpers.CreateDetailsViewModel();
        var control = _sut.Build(vm);

        Assert.IsType<DetailsView>(control);
        Assert.Equal(vm, control!.DataContext);
    }

    [AvaloniaFact]
    public void Build_AboutViewModel_ReturnsAboutView()
    {
        var vm = new AboutViewModel(Substitute.For<ILogger<AboutViewModel>>());
        var control = _sut.Build(vm);

        Assert.IsType<AboutView>(control);
        Assert.Equal(vm, control!.DataContext);
    }

    [AvaloniaFact]
    public void Build_UnknownType_ReturnsNotFoundTextBlock()
    {
        var control = _sut.Build("unknown");

        var textBlock = Assert.IsType<TextBlock>(control);
        Assert.Contains("Not Found", textBlock.Text);
    }

    [Fact]
    public void Build_Null_ReturnsNull()
    {
        Assert.Null(_sut.Build(null));
    }
}
