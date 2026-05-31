using Peerfluence.Core.Config;
using System.IO.Abstractions;
using System.Text.Json;

namespace Peerfluence.Core.Services;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private readonly IAppPaths _paths;
    private readonly IFileSystem _fileSystem;
    private readonly JsonSerializerOptions _serializerOptions;

    public JsonAppSettingsStore(IAppPaths paths, IFileSystem fileSystem)
    {
        _paths = paths;
        _fileSystem = fileSystem;
        _serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = AppSettingsJsonContext.Default
        };
    }

    public async Task<AppSettings?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!_fileSystem.File.Exists(_paths.SettingsFilePath))
        {
            return null;
        }

        try
        {
            await using var stream = _fileSystem.File.OpenRead(_paths.SettingsFilePath);
            if (stream.Length == 0) return null;

            return await JsonSerializer.DeserializeAsync(
                stream,
                AppSettingsJsonContext.Default.AppSettings,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        _fileSystem.Directory.CreateDirectory(_paths.AppDataDirectory);

        await using var stream = _fileSystem.File.Create(_paths.SettingsFilePath);
        await JsonSerializer.SerializeAsync(
            stream,
            settings,
            AppSettingsJsonContext.Default.AppSettings,
            cancellationToken).ConfigureAwait(false);
    }
}

