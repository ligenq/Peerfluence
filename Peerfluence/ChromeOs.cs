using System;
using System.IO;

namespace Peerfluence;

internal static class ChromeOs
{
    /// <summary>
    /// Whether this runs as a Linux app on ChromeOS, whose window frame shows no title.
    ///
    /// <para>
    /// ChromeOS draws the frame of every Linux app itself, and draws it without the window's
    /// title, though the title reaches it: a plain X11 window shows a blank title bar there too.
    /// A dialog that relies on its title bar to say what it is for says nothing, so dialogs show
    /// their title in their own content instead, and only here, where nothing else shows it.
    /// </para>
    /// <para>
    /// ChromeOS puts its milestone in every Linux container it starts, which no ordinary Linux
    /// system has.
    /// </para>
    /// </summary>
    public static bool HidesWindowTitles { get; } =
        OperatingSystem.IsLinux() && File.Exists("/dev/.cros_milestone");
}
