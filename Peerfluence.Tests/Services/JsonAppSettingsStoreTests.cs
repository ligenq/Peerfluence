using System.IO.Abstractions;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

public sealed class JsonAppSettingsStoreTests : IDisposable
{
    [Fact]
    public async Task StreamingPolicy_SurvivesASourceGeneratedJsonRoundTrip()
    {
        var store = new JsonAppSettingsStore(_paths, new FileSystem());
        var settings = new AppSettings
        {
            Streaming =
            {
                ReadAheadBytes = 80L * 1024 * 1024,
                ReadAheadSeconds = 45,
                RequestQueueSecondsWhileBuffering = 0,
                DataWaitTimeoutSeconds = 0,
                MetadataWaitTimeoutSeconds = 120,
                BindAddress = "192.168.1.23",
                Port = 8087,
            },
        };
        await store.SaveAsync(settings, TestContext.Current.CancellationToken);
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(loaded);
        Assert.Equal(80L * 1024 * 1024, loaded.Streaming.ReadAheadBytes);
        Assert.Equal(45, loaded.Streaming.ReadAheadSeconds);
        Assert.Equal(0, loaded.Streaming.RequestQueueSecondsWhileBuffering);
        Assert.Equal(0, loaded.Streaming.DataWaitTimeoutSeconds);
        Assert.Equal(120, loaded.Streaming.MetadataWaitTimeoutSeconds);
        Assert.Equal("192.168.1.23", loaded.Streaming.BindAddress);
        Assert.Equal(8087, loaded.Streaming.Port);
    }

    private readonly string _rootPath;
    private readonly TestAppPaths _paths;

    public JsonAppSettingsStoreTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"peerfluence-json-settings-tests-{Guid.NewGuid():N}");
        _paths = new TestAppPaths(
            _rootPath,
            Path.Combine(_rootPath, "downloads"),
            Path.Combine(_rootPath, "session"),
            Path.Combine(_rootPath, "settings.json"));

        Directory.CreateDirectory(_rootPath);
    }

    [Fact]
    public async Task LoadAsync_ReturnsSavedSettings()
    {
        var sut = new JsonAppSettingsStore(_paths, new FileSystem());
        var settings = new AppSettings
        {
            Language = "sv-SE"
        };

        await sut.SaveAsync(settings, TestContext.Current.CancellationToken);

        var loaded = await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal("sv-SE", loaded.Language);
    }

    [Fact]
    public async Task LoadAsync_PreservesInvalidJsonBeforeReturningNull()
    {
        const string invalidJson = """{ "Language": """;
        await File.WriteAllTextAsync(_paths.SettingsFilePath, invalidJson, TestContext.Current.CancellationToken);

        var sut = new JsonAppSettingsStore(_paths, new FileSystem());

        var loaded = await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Null(loaded);
        var backupPath = Assert.Single(Directory.EnumerateFiles(_rootPath, "settings.invalid-*.json"));
        Assert.Equal(invalidJson, await File.ReadAllTextAsync(backupPath, TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// A derived value has no business in the file. It would be a second answer to a question the
    /// address already answers, and one that reads back into nothing.
    /// </summary>
    [Fact]
    public async Task SaveAsync_DoesNotWriteValuesThatAreDerivedFromOtherSettings()
    {
        var sut = new JsonAppSettingsStore(_paths, new FileSystem());
        var settings = new AppSettings();
        settings.Search.TorznabUrl = "http://example.invalid/api";

        await sut.SaveAsync(settings, TestContext.Current.CancellationToken);

        var json = await File.ReadAllTextAsync(_paths.SettingsFilePath, TestContext.Current.CancellationToken);

        Assert.Contains("TorznabUrl", json);
        Assert.DoesNotContain("IsConfigured", json);
        Assert.DoesNotContain("IsUsable", json);
        Assert.DoesNotContain("RequiresAuthentication", json);
    }

    private sealed record TestAppPaths(
        string AppDataDirectory,
        string DefaultDownloadDirectory,
        string SessionDirectory,
        string SettingsFilePath) : IAppPaths;
}
