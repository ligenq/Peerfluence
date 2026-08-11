using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace Peerfluence.Services;

/// <summary>
/// What the application's window can do, rather than the window itself.
///
/// <para>
/// Deliberately hands out no <see cref="TopLevel"/>. A caller given one reaches straight into
/// Avalonia for a clipboard, an owner or a render target, and takes on something no test can
/// substitute; the awkward parts belong on this side of the interface, where a substitute can
/// stand in for all of them.
/// </para>
/// </summary>
public interface ITopLevelService
{
    /// <summary>
    /// Whether there is a window at all. False before startup finishes, and in headless runs.
    /// </summary>
    bool IsWindowAvailable { get; }

    /// <summary>
    /// Gets the clipboard provider.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">Thrown when there is no window yet.</exception>
    IClipboard GetClipboard();

    /// <summary>
    /// Gets the storage provider for file system access.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">Thrown when there is no window yet.</exception>
    IStorageProvider GetStorageProvider();

    /// <summary>
    /// Shows <paramref name="window"/> as a modal dialog of the application's window, or on its own
    /// when there is no window to own it. Completes once the dialog closes, either way.
    /// </summary>
    Task ShowDialogAsync(Window window);

    /// <summary>
    /// Renders the application's window to a PNG. Null when the window has no area to render, which
    /// is what a window that has not been laid out yet reports.
    /// </summary>
    Task<byte[]?> CaptureWindowPngAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the current TopLevel instance.
    /// </summary>
    void SetTopLevel(TopLevel? topLevel);
}
