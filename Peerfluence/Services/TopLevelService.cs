using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace Peerfluence.Services;

public sealed class TopLevelService : ITopLevelService
{
    private TopLevel? _topLevel;

    public IClipboard GetClipboard()
    {
        return _topLevel?.Clipboard ?? throw new InvalidOperationException("TopLevel has not been initialized.");
    }

    public IStorageProvider GetStorageProvider()
    {
        return _topLevel?.StorageProvider ?? throw new InvalidOperationException("TopLevel has not been initialized.");
    }

    public void SetTopLevel(TopLevel? topLevel)
    {
        _topLevel = topLevel;
    }

    public TopLevel? GetTopLevel()
    {
        return _topLevel;
    }
}
