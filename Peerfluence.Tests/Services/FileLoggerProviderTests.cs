using Microsoft.Extensions.Logging;
using Peerfluence.Logging;

namespace Peerfluence.Tests.Services;

public class FileLoggerProviderTests : IDisposable
{
    private readonly string _logPath;

    public FileLoggerProviderTests()
    {
        _logPath = Path.Combine(Path.GetTempPath(), $"peerfluence_test_{Guid.NewGuid():N}.log");
    }

    public void Dispose()
    {
        try { File.Delete(_logPath); } catch { }
        foreach (var path in Directory.EnumerateFiles(Path.GetDirectoryName(_logPath)!, $"{Path.GetFileNameWithoutExtension(_logPath)}.*.log"))
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void CreateLogger_ReturnsLoggerInstance()
    {
        using var provider = new FileLoggerProvider(_logPath);
        var logger = provider.CreateLogger("TestCategory");

        Assert.NotNull(logger);
    }

    [Fact]
    public void Logger_WritesToFile()
    {
        using (var provider = new FileLoggerProvider(_logPath))
        {
            var logger = provider.CreateLogger("TestCategory");
            logger.LogInformation("Hello from test");
        }

        var content = File.ReadAllText(_logPath);
        Assert.Contains("[Information] TestCategory: Hello from test", content);
    }

    [Fact]
    public void Logger_IncludesTimestamp()
    {
        using (var provider = new FileLoggerProvider(_logPath))
        {
            var logger = provider.CreateLogger("Test");
            logger.LogWarning("timestamp check");
        }

        var content = File.ReadAllText(_logPath);
        // Timestamp format: yyyy-MM-dd HH:mm:ss.fff
        Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}", content);
    }

    [Fact]
    public void Logger_IncludesExceptionDetails()
    {
        using (var provider = new FileLoggerProvider(_logPath))
        {
            var logger = provider.CreateLogger("Test");
            logger.LogError(new InvalidOperationException("boom"), "Something failed");
        }

        var content = File.ReadAllText(_logPath);
        Assert.Contains("Something failed", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("boom", content);
    }

    [Fact]
    public void Logger_ArchivesPreviousLogOnNewSession()
    {
        // First session
        using (var provider = new FileLoggerProvider(_logPath))
        {
            var logger = provider.CreateLogger("Session1");
            logger.LogInformation("first session message");
        }

        // Second session archives the previous active log and starts a fresh one.
        using (var provider = new FileLoggerProvider(_logPath))
        {
            var logger = provider.CreateLogger("Session2");
            logger.LogInformation("second session message");
        }

        var content = File.ReadAllText(_logPath);
        Assert.DoesNotContain("first session message", content);
        Assert.Contains("second session message", content);

        var archivePath = Assert.Single(Directory.EnumerateFiles(
            Path.GetDirectoryName(_logPath)!,
            $"{Path.GetFileNameWithoutExtension(_logPath)}.*.log"));
        var archiveContent = File.ReadAllText(archivePath);
        Assert.Contains("first session message", archiveContent);
    }

    [Fact]
    public void Logger_RetainsOnlyRecentArchives()
    {
        for (var i = 0; i < 7; i++)
        {
            using var provider = new FileLoggerProvider(_logPath);
            var logger = provider.CreateLogger($"Session{i}");
            logger.LogInformation("session {Index}", i);
            Thread.Sleep(5);
        }

        var archives = Directory.EnumerateFiles(
            Path.GetDirectoryName(_logPath)!,
            $"{Path.GetFileNameWithoutExtension(_logPath)}.*.log");

        Assert.Equal(5, archives.Count());
    }

    [Fact]
    public void Logger_IgnoresLogLevelNone()
    {
        using var provider = new FileLoggerProvider(_logPath);
        var logger = provider.CreateLogger("Test");

        Assert.False(logger.IsEnabled(LogLevel.None));
    }

    [Fact]
    public void Logger_IsEnabledForAllLevelsExceptNone()
    {
        using var provider = new FileLoggerProvider(_logPath);
        var logger = provider.CreateLogger("Test");

        Assert.True(logger.IsEnabled(LogLevel.Trace));
        Assert.True(logger.IsEnabled(LogLevel.Debug));
        Assert.True(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.True(logger.IsEnabled(LogLevel.Error));
        Assert.True(logger.IsEnabled(LogLevel.Critical));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var provider = new FileLoggerProvider(_logPath);
        provider.Dispose();
        provider.Dispose(); // Should not throw
    }

    [Fact]
    public void Logger_DoesNotWriteAfterDispose()
    {
        var provider = new FileLoggerProvider(_logPath);
        var logger = provider.CreateLogger("Test");
        logger.LogInformation("before dispose");
        provider.Dispose();

        // Should not throw, just silently ignored
        logger.LogInformation("after dispose");

        var content = File.ReadAllText(_logPath);
        Assert.Contains("before dispose", content);
        Assert.DoesNotContain("after dispose", content);
    }
}
