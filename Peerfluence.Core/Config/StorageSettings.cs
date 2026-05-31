namespace Peerfluence.Core.Config;

public sealed class StorageSettings
{
    public string DownloadPath { get; set; } = string.Empty;

    public string SessionPath { get; set; } = string.Empty;

    public bool EnableSessionPersistence { get; set; } = true;
}

