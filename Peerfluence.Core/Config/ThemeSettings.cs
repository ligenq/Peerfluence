namespace Peerfluence.Core.Config;

public sealed class ThemeSettings
{
    /// <summary>
    /// Follows the operating system: Midnight when it is dark, Midnight Light when it is light.
    /// </summary>
    public const string DefaultThemeVariant = "System";

    /// <summary>The accent a new profile starts with, which both Midnight variants offer.</summary>
    public const string DefaultColorTheme = "Cobalt";

    /// <summary>The defaults a profile saved by this version has been brought up to.</summary>
    public const int CurrentDefaultsRevision = 1;

    public string ThemeVariant { get; set; } = DefaultThemeVariant;

    public string ColorTheme { get; set; } = DefaultColorTheme;

    /// <summary>
    /// Which defaults this profile has been brought up to. Absent, and so zero, in a profile saved
    /// before there were any to bring it up to.
    /// </summary>
    public int DefaultsRevision { get; set; }

    /// <summary>
    /// Brings a profile saved under older defaults up to the current ones, once.
    /// </summary>
    /// <returns>Whether anything changed, so the profile needs saving.</returns>
    /// <remarks>
    /// <para>
    /// Revision 1: the default accent moved from Teal to Cobalt. A profile stores the defaults it
    /// started with, so a saved Teal cannot be told apart from a chosen one, and every Teal moves.
    /// </para>
    /// <para>
    /// Once only, which is what the revision is for: a Teal chosen after the move is a choice, and
    /// stays. A profile from a later version, with a higher revision, is left as it is.
    /// </para>
    /// </remarks>
    public bool ApplyNewerDefaults()
    {
        if (DefaultsRevision >= CurrentDefaultsRevision)
        {
            return false;
        }

        if (string.Equals(ColorTheme, "Teal", StringComparison.OrdinalIgnoreCase))
        {
            ColorTheme = DefaultColorTheme;
        }

        DefaultsRevision = CurrentDefaultsRevision;
        return true;
    }
}
