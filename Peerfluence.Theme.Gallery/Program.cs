using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Peerfluence.Theme.Gallery;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var builder = AppBuilder.Configure<GalleryApp>().WithInterFont();
        if (args.Length == 0)
        {
            return builder.UsePlatformDetect().StartWithClassicDesktopLifetime(args);
        }

        if (args.Length is not (2 or 3) || args[0] != "--capture"
            || (args.Length == 3 && args[2] != "--all-accents"))
        {
            Console.Error.WriteLine("Usage: dotnet run --project Peerfluence.Theme.Gallery [--capture <directory> [--all-accents]]");
            return 1;
        }

        builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        GalleryCapture.Write(args[1], args.Length == 3);
        return 0;
    }
}

public sealed class GalleryApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());
        Styles.Add(new PeerfluenceTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new GalleryWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
