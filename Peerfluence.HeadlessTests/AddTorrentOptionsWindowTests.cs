using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Views;

namespace Peerfluence.HeadlessTests;

/// <summary>
/// The add-torrent dialog, loaded for real.
/// </summary>
/// <remarks>
/// Constructing the window parses its XAML and resolves every <c>StaticResource</c> in it. Nothing
/// did that before, which is how six references to a spacing key that was never defined survived in
/// here - the compiler does not resolve resource keys, so the first thing that notices is whoever
/// opens the dialog.
/// </remarks>
public class AddTorrentOptionsWindowTests
{
    [AvaloniaFact]
    public void TheWindow_LoadsWithEveryResourceItReferences()
    {
        var window = new AddTorrentOptionsWindow();

        Assert.NotNull(window);
    }
}
