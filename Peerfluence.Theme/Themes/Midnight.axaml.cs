using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Peerfluence.Theme;

/// <summary>
/// The styles that apply only while the Midnight variant is selected.
/// </summary>
/// <remarks>
/// Held apart from the rest of the theme, and added or removed by
/// <see cref="PeerfluenceTheme.UseVariant"/>, because these are the parts of a variant that cannot
/// be expressed as per-variant resources: a typeface, and metrics that are otherwise shared by
/// every variant. See the file's own comments for why each one is here rather than in the palette.
/// </remarks>
public sealed class MidnightStyles : Styles
{
    public MidnightStyles(IServiceProvider? serviceProvider = null)
    {
        AvaloniaXamlLoader.Load(serviceProvider, this);
    }
}
