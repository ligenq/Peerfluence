namespace Peerfluence.Core.Services;

public interface IUpdateService
{
    bool IsUpdateAvailable { get; }

    string? AvailableVersion { get; }

    bool IsInstalled { get; }

    Task<bool> CheckForUpdatesAsync();

    Task<bool> DownloadUpdateAsync(Action<int>? progress = null, CancellationToken cancellationToken = default);

    void ApplyUpdateAndRestart(string[]? restartArgs = null);
}
