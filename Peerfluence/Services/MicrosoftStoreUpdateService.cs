using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Peerfluence.Services;

public sealed class MicrosoftStoreUpdateService : IUpdateService
{
    private readonly ILogger<MicrosoftStoreUpdateService> _logger;

    public MicrosoftStoreUpdateService(ILogger<MicrosoftStoreUpdateService> logger)
    {
        _logger = logger;
    }

    public UpdateChannel Channel => UpdateChannel.MicrosoftStore;

    public bool IsUpdateAvailable => false;

    public string? AvailableVersion => null;

    public bool IsInstalled => true;

    public bool CanCheckForUpdates => false;

    public bool CanApplyUpdates => false;

    public Task<bool> CheckForUpdatesAsync()
    {
        _logger.LogDebug("Skipping update check because updates are managed by Microsoft Store.");
        return Task.FromResult(false);
    }

    public Task<bool> DownloadUpdateAsync(Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Skipping update download because updates are managed by Microsoft Store.");
        return Task.FromResult(false);
    }

    public void ApplyUpdateAndRestart(string[]? restartArgs = null)
    {
        _logger.LogDebug("Skipping update apply because updates are managed by Microsoft Store.");
    }
}
