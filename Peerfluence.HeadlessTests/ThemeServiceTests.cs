using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Peerfluence.Core.Config;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Services;
using Peerfluence.Theme;

namespace Peerfluence.HeadlessTests;

/// <summary>
/// Headless rather than a plain unit test, and that is the whole point: <c>ApplyPalette</c> returns
/// early when there is no Application, so without one running these never reach the lookup that
/// threw. A version of this in the unit project passed whether or not the fix was present.
/// </summary>
public sealed class ThemeServiceTests
{
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Aubergine")]
    public void AThemeItCannotName_IsIgnoredRatherThanThrown(string? colorTheme)
    {
        // The name comes from settings, so it can be absent, blank, or written by a version that
        // knew more themes than this one.
        using var service = new ThemeService();
        service.Apply(new ThemeSettings
        {
            ThemeVariant = "System",
            ColorTheme = colorTheme!
        });
    }

    [AvaloniaFact]
    public void AVariantAndBackgroundItCannotName_AreIgnoredToo()
    {
        using var service = new ThemeService();
        service.Apply(new ThemeSettings
        {
            ThemeVariant = null!,
            ColorTheme = null!
        });
    }

    [AvaloniaFact]
    public void SystemAppearanceChanges_UpdateOpenWindowsAndPreserveTheAccent()
    {
        var platform = Substitute.For<IPlatformSettings>();
        platform.GetColorValues().Returns(new PlatformColorValues { ThemeVariant = PlatformThemeVariant.Light });
        using var service = new ThemeService(platform);
        var app = Application.Current!;
        var previous = app.RequestedThemeVariant;
        var theme = app.Styles.OfType<PeerfluenceTheme>().Single();
        var settings = new ThemeSettings { ThemeVariant = "System", ColorTheme = "Sky" };
        var window = new Window { Width = 400, Height = 200 };
        try
        {
            service.Apply(settings);
            service.Apply(settings);
            window.Show();
            Assert.Equal(PeerfluenceTheme.MidnightLight, window.ActualThemeVariant);

            platform.ClearReceivedCalls();
            // Notifications can arrive outside the UI thread. Updating the window must be
            // dispatched, and repeated Apply calls must not accumulate handlers.
            ChangeAppearanceInBackground(platform, PlatformThemeVariant.Dark);
            Dispatcher.UIThread.RunJobs();
            platform.Received(1).GetColorValues();
            Assert.Equal(PeerfluenceTheme.Midnight, window.ActualThemeVariant);
            Assert.Equal(Color.Parse("#1C1856"), Assert.IsType<SolidColorBrush>(window.Background).Color);
            Assert.Contains("Open Sans", window.FontFamily.ToString(), StringComparison.Ordinal);
            Assert.Equal("System", settings.ThemeVariant);
            Assert.Equal("Sky", settings.ColorTheme);
            Assert.True(theme.TryGetResource("SkyAccentColor", PeerfluenceTheme.Midnight, out var expected));
            Assert.True(theme.TryGetResource("PfAccentColor", PeerfluenceTheme.Midnight, out var actual));
            Assert.Equal(expected, actual);

            ChangeAppearance(platform, PlatformThemeVariant.Light);
            Assert.Equal(PeerfluenceTheme.MidnightLight, window.ActualThemeVariant);
        }
        finally
        {
            service.Dispose();
            window.Close();
            theme.UseVariant(previous);
            theme.UseAccent(PeerfluenceTheme.DefaultAccent);
            app.RequestedThemeVariant = previous;
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedSystemChanges_AreIgnoredAfterAnExplicitChoiceOrDisposal(bool dispose)
    {
        var platform = Substitute.For<IPlatformSettings>();
        platform.GetColorValues().Returns(new PlatformColorValues { ThemeVariant = PlatformThemeVariant.Light });
        using var service = new ThemeService(platform);
        var app = Application.Current!;
        var previous = app.RequestedThemeVariant;
        var theme = app.Styles.OfType<PeerfluenceTheme>().Single();
        try
        {
            service.Apply(new ThemeSettings { ThemeVariant = "System" });
            ChangeAppearanceInBackground(platform, PlatformThemeVariant.Dark);
            Assert.Equal(PeerfluenceTheme.MidnightLight, app.RequestedThemeVariant);
            if (dispose)
            {
                service.Dispose();
            }
            else
            {
                service.Apply(new ThemeSettings { ThemeVariant = "MidnightLight" });
            }

            platform.ClearReceivedCalls();
            Dispatcher.UIThread.RunJobs();
            ChangeAppearance(platform, PlatformThemeVariant.Dark);
            platform.DidNotReceive().GetColorValues();
            Assert.Equal(PeerfluenceTheme.MidnightLight, app.RequestedThemeVariant);

            if (!dispose)
            {
                service.Apply(new ThemeSettings { ThemeVariant = "System" });
                Assert.Equal(PeerfluenceTheme.Midnight, app.RequestedThemeVariant);
                ChangeAppearance(platform, PlatformThemeVariant.Light);
                Assert.Equal(PeerfluenceTheme.MidnightLight, app.RequestedThemeVariant);
            }
        }
        finally
        {
            service.Dispose();
            theme.UseVariant(previous);
            app.RequestedThemeVariant = previous;
        }
    }

    private static void ChangeAppearance(IPlatformSettings platform, PlatformThemeVariant variant)
    {
        var values = new PlatformColorValues { ThemeVariant = variant };
        platform.GetColorValues().Returns(values);
        platform.ColorValuesChanged += Raise.Event<EventHandler<PlatformColorValues>>(platform, values);
    }

    private static void ChangeAppearanceInBackground(IPlatformSettings platform, PlatformThemeVariant variant)
    {
        // A dedicated thread prevents a synchronous task wait from inlining the notification
        // onto the UI thread, which would skip the dispatcher path this test needs to exercise.
        var thread = new Thread(() => ChangeAppearance(platform, variant));
        thread.Start();
        thread.Join();
    }
}
