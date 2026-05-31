using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace Peerfluence.Services;

public interface ITopLevelService
{
    /// <summary>
    /// Gets the clipboard provider.
    /// </summary>
    IClipboard GetClipboard();

    /// <summary>
    /// Gets the storage provider for file system access.
    /// </summary>
    IStorageProvider GetStorageProvider();

    /// <summary>
    /// Sets the current TopLevel instance.
    /// </summary>
    void SetTopLevel(TopLevel? topLevel);

    /// <summary>
    /// Gets the current TopLevel instance.
    /// </summary>
    TopLevel? GetTopLevel();
}
