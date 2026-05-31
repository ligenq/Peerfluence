using Avalonia.Controls;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Services;
using Peerfluence.Services.Mcp;
using Peerfluence.Core.Services;

namespace Peerfluence.HeadlessTests;

public sealed class McpToolHandlerHeadlessTests
{
    [AvaloniaFact]
    public async Task TakeScreenshotAsync_ReturnsImage_WhenWindowIsAvailable()
    {
        var window = new Window
        {
            Content = new Border
            {
                Width = 200,
                Height = 100
            },
            Width = 400,
            Height = 300
        };

        window.Show();

        var topLevelService = Substitute.For<ITopLevelService>();
        topLevelService.GetTopLevel().Returns(window);

        var sut = new McpToolHandler(
            Substitute.For<ITorrentService>(),
            topLevelService,
            Substitute.For<IAppSettingsService>(),
            Substitute.For<IHostApplicationLifetime>());

        var result = await sut.TakeScreenshotAsync();

        Assert.Single(result.Content);
        Assert.IsType<ImageContentBlock>(result.Content[0]);

        window.Close();
    }

    [AvaloniaFact]
    public async Task TakeScreenshotAsync_ReturnsError_WhenUiWindowUnavailable()
    {
        var topLevelService = Substitute.For<ITopLevelService>();
        topLevelService.GetTopLevel().Returns((TopLevel?)null);

        var sut = new McpToolHandler(
            Substitute.For<ITorrentService>(),
            topLevelService,
            Substitute.For<IAppSettingsService>(),
            Substitute.For<IHostApplicationLifetime>());

        var result = await sut.TakeScreenshotAsync();

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Contains("UI window not available", text.Text);
    }
}
