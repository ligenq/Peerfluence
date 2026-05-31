using Microsoft.Extensions.Logging;
using Peerfluence.Core.Services;
using Peerfluence.Services.Mcp;

namespace Peerfluence.Tests.Services.Mcp;

public class McpResourceHandlerTests : IDisposable
{
    private readonly IAppPaths _appPaths;
    private readonly string _testLogDir;

    public McpResourceHandlerTests()
    {
        _appPaths = new AppPaths();
        _testLogDir = _appPaths.AppDataDirectory;
        if (!Directory.Exists(_testLogDir))
        {
            Directory.CreateDirectory(_testLogDir);
        }
    }

    [Fact]
    public async Task GetLatestLogsAsync_ReturnsLogContent_WhenLogFileExists()
    {
        // Arrange
        var logger = Substitute.For<ILogger<McpResourceHandler>>();
        // We pass null for TorrentEngineService and TorrentService as they are not used in GetLatestLogsAsync
        var handler = new McpResourceHandler(null!, _appPaths, logger);

        var logFilePath = Path.Combine(_testLogDir, "test_log_123.log");
        await File.WriteAllTextAsync(logFilePath, "Test Log Line 1\nTest Log Line 2");

        try
        {
            // Act
            var result = await handler.GetLatestLogsAsync();

            // Assert
            Assert.Contains("Test Log Line 1", result);
            Assert.Contains("Test Log Line 2", result);
        }
        finally
        {
            if (File.Exists(logFilePath))
            {
                File.Delete(logFilePath);
            }
        }
    }

    [Fact]
    public async Task GetEngineStatsAsync_ReturnsError_WhenEngineIsNull()
    {
        // Arrange
        var logger = Substitute.For<ILogger<McpResourceHandler>>();
        var handler = new McpResourceHandler(null!, _appPaths, logger);

        // Act
        var result = await handler.GetEngineStatsAsync();

        // Assert
        Assert.Contains("Error getting stats", result);
    }

    [Theory]
    [InlineData(nameof(McpResourceHandler.GetTorrentFilesAsync))]
    [InlineData(nameof(McpResourceHandler.GetTorrentPeersAsync))]
    public void TorrentResourceHandler_ParameterNameMatchesUriTemplate(string methodName)
    {
        var method = typeof(McpResourceHandler).GetMethod(methodName);
        var parameter = Assert.Single(method!.GetParameters());

        Assert.Equal("infoHash", parameter.Name);
    }

    public void Dispose()
    {
        // Cleanup generated test log files if any
        foreach (var file in Directory.GetFiles(_testLogDir, "*.log"))
        {
            try { File.Delete(file); } catch { /* ignore */ }
        }
    }
}
