namespace Peerfluence.Core.Services;

public interface IUpdateService
{
    UpdateChannel Channel { get; }

    bool IsUpdateAvailable { get; }

    string? AvailableVersion { get; }

    bool IsInstalled { get; }

    bool CanCheckForUpdates { get; }

    bool CanApplyUpdates { get; }

    Task<bool> CheckForUpdatesAsync();

    Task<bool> DownloadUpdateAsync(Action<int>? progress = null, CancellationToken cancellationToken = default);

    void ApplyUpdateAndRestart(string[]? restartArgs = null);
}
