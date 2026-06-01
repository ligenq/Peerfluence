namespace Peerfluence.Core.Config;

public sealed class UpdateSettings
{
    public const string DefaultUpdateUrl = "https://github.com/ligenq/Peerfluence";

    public string UpdateUrl { get; set; } = DefaultUpdateUrl;

    public bool CheckForUpdatesOnStartup { get; set; } = true;
}

