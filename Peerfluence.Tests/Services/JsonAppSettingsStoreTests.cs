using System.IO.Abstractions;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

public sealed class JsonAppSettingsStoreTests : IDisposable
{
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

    private sealed record TestAppPaths(
        string AppDataDirectory,
        string DefaultDownloadDirectory,
        string SessionDirectory,
        string SettingsFilePath) : IAppPaths;
}
