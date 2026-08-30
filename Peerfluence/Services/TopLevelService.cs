using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Peerfluence.Services;

public sealed class TopLevelService : ITopLevelService
{
    private TopLevel? _topLevel;

    public bool IsWindowAvailable => _topLevel != null;

    /// <summary>The clipboard, which only exists on the thread the window does.</summary>
    public IClipboard GetClipboard()
    {
        Dispatcher.UIThread.VerifyAccess();
        return _topLevel?.Clipboard ?? throw new InvalidOperationException("TopLevel has not been initialized.");
    }

    /// <summary>The file pickers, which only exist on the thread the window does.</summary>
    public IStorageProvider GetStorageProvider()
    {
        Dispatcher.UIThread.VerifyAccess();
        return _topLevel?.StorageProvider ?? throw new InvalidOperationException("TopLevel has not been initialized.");
    }

    public async Task ShowDialogAsync(Window window)
    {
        Dispatcher.UIThread.VerifyAccess();

        ArgumentNullException.ThrowIfNull(window);

        if (_topLevel is Window owner)
        {
            await window.ShowDialog(owner);
            return;
        }

        // Nothing to own it, so show it standalone - but still finish when it closes, so awaiting
        // this means the same thing whether or not there was an owner.
        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        await closed.Task;
    }

    public async Task<byte[]?> CaptureWindowPngAsync(CancellationToken cancellationToken = default)
    {
        var topLevel = _topLevel;
        if (topLevel == null)
        {
            return null;
        }

        var bitmap = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var width = topLevel.Bounds.Width;
            var height = topLevel.Bounds.Height;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            var bmp = new RenderTargetBitmap(new PixelSize((int)width, (int)height), new Vector(96, 96));
            bmp.Render(topLevel);
            return bmp;
        });

        if (bitmap == null)
        {
            return null;
        }

        using (bitmap)
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, new PngBitmapEncoderOptions());
            return stream.ToArray();
        }
    }

    public void SetTopLevel(TopLevel? topLevel)
    {
        _topLevel = topLevel;
    }
}
