namespace Peerfluence.Core.Config;

public sealed class UpdateSettings
{
    public string UpdateUrl { get; set; } = string.Empty;

    public bool CheckForUpdatesOnStartup { get; set; } = true;
}

