using System.Globalization;
using FlaUI.Core.AutomationElements;

namespace Peerfluence.UiTests;

/// <summary>
/// Captures a PNG of every navigation destination, so a visual change can be reviewed rather than
/// described.
/// </summary>
/// <remarks>
/// <para>
/// Not a test: it asserts almost nothing and exists to produce artifacts. It lives here because
/// this is the only project that can drive the real application, and it follows the same opt-in
/// gate as the live-download tests so that an ordinary <c>dotnet test</c> run never starts it.
/// </para>
/// <para>
/// Safe on a machine that also uses Peerfluence, because <see cref="RunningApplication"/> starts a
/// throwaway profile and releases the single-instance lock. Nothing here touches a real session,
/// real settings or the network.
/// </para>
/// <para>
/// Run it with:
/// <c>PEERFLUENCE_SCREENSHOTS=1 PEERFLUENCE_SCREENSHOT_DIR=&lt;dir&gt; dotnet test
/// Peerfluence.UiTests --filter ScreenshotGallery</c>
/// </para>
/// </remarks>
public sealed class ScreenshotGallery
{
    private const int Width = 1360;
    private const int Height = 900;

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task CaptureStreamingOptions(string variant)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PEERFLUENCE_SCREENSHOTS") == "1",
            "Set PEERFLUENCE_SCREENSHOTS=1 to capture the interface.");
        string directory = Environment.GetEnvironmentVariable("PEERFLUENCE_SCREENSHOT_DIR")
            ?? Path.Combine(Path.GetTempPath(), "peerfluence-screenshots");
        Directory.CreateDirectory(directory);
        string settings = $$"""
            { "Theme": { "ThemeVariant": "{{variant}}", "ColorTheme": "Teal" }, "InterfaceMode": "Advanced" }
            """;
        using var application = new RunningApplication(settings);
        Resize(application.Window);
        RunningApplication.Activate(application.NavigationItems().Single(item => item.Name == "Settings"));
        application.Find("StorageAndSessionTab").AsTabItem().Select();
        var options = application.Find("StreamingOptionsExpander");
        options.Patterns.ExpandCollapse.Pattern.Expand();
        application.Find("StreamingMetadataTimeoutSecondsInput").Focus();
        Thread.Sleep(700);
        await Capture(application, directory, $"{Slug(variant)}-streaming-options", []);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task CaptureAboutLegalNotice(string variant)
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("PEERFLUENCE_SCREENSHOTS") == "1",
            "Set PEERFLUENCE_SCREENSHOTS=1 to capture the interface. This starts a real window.");

        string directory = Environment.GetEnvironmentVariable("PEERFLUENCE_SCREENSHOT_DIR")
            ?? Path.Combine(Path.GetTempPath(), "peerfluence-screenshots");
        Directory.CreateDirectory(directory);
        string settings = $$"""
            { "Theme": { "ThemeVariant": "{{variant}}", "ColorTheme": "Teal" }, "InterfaceMode": "Advanced" }
            """;
        using var application = new RunningApplication(settings);
        Resize(application.Window);
        application.Find("ShowAboutButton").AsButton().Invoke();
        RunningApplication.Until(() => application.Exists("OpenGitHubButton"), "the About page");
        Thread.Sleep(900);

        var captured = new List<string>();
        string prefix = $"{Slug(variant)}-about";
        await Capture(application, directory, $"{prefix}-collapsed", captured);
        application.Find("LegalNoticeExpander").Patterns.ExpandCollapse.Pattern.Expand();
        Thread.Sleep(900);
        await Capture(application, directory, $"{prefix}-expanded", captured);
    }

    /// <summary>
    /// The dark variant, on a profile that already asked for it.
    /// </summary>
    /// <remarks>
    /// Seeded rather than driven through the settings screen, and separate from the light capture
    /// because the theme supports both and only one of them is whatever this machine happens to be
    /// set to. A theme reviewed in one variant is a theme half reviewed.
    /// </remarks>
    [Fact]
    public async Task CaptureDarkVariant()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("PEERFLUENCE_SCREENSHOTS") == "1",
            "Set PEERFLUENCE_SCREENSHOTS=1 to capture the interface. This starts a real window.");

        string directory = Environment.GetEnvironmentVariable("PEERFLUENCE_SCREENSHOT_DIR")
            ?? Path.Combine(Path.GetTempPath(), "peerfluence-screenshots");
        Directory.CreateDirectory(directory);

        const string settings = """
            { "Theme": { "ThemeVariant": "Dark", "ColorTheme": "Teal" }, "InterfaceMode": "Advanced" }
            """;

        using var application = new RunningApplication(settings);
        Resize(application.Window);
        Thread.Sleep(1800);

        var captured = new List<string>();
        await Capture(application, directory, "dark-00-start", captured);

        var destinations = application.NavigationItems();
        for (var i = 0; i < destinations.Length; i++)
        {
            var items = application.NavigationItems();
            if (i >= items.Length)
            {
                break;
            }

            string name = items[i].Properties.Name.ValueOrDefault ?? $"destination-{i}";
            RunningApplication.Activate(items[i]);
            Thread.Sleep(900);
            await Capture(application, directory, $"dark-{i + 1:D2}-{Slug(name)}", captured);
        }

        Assert.NotEmpty(captured);
    }

    [Fact]
    public async Task CaptureEveryDestination()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("PEERFLUENCE_SCREENSHOTS") == "1",
            "Set PEERFLUENCE_SCREENSHOTS=1 to capture the interface. This starts a real window.");

        string directory = Environment.GetEnvironmentVariable("PEERFLUENCE_SCREENSHOT_DIR")
            ?? Path.Combine(Path.GetTempPath(), "peerfluence-screenshots");
        Directory.CreateDirectory(directory);

        using var application = new RunningApplication();
        Resize(application.Window);
        Thread.Sleep(1200);

        var captured = new List<string>();

        // A throwaway profile is a first run, so the interface picker is what is on screen and it
        // blocks navigation. Photograph it, then choose the advanced interface, which is the one
        // with something to look at.
        await Capture(application, directory, "00-first-run", captured);
        application.Find("ChooseAdvancedModeButton").AsButton().Invoke();
        RunningApplication.Until(() => application.Exists("AddMagnetButton"), "the advanced interface");

        // Let the shell settle before capturing: a still-laying-out window photographs as a
        // half-built one, which then reads as a visual defect that is not there.
        Thread.Sleep(1500);
        await Capture(application, directory, "01-downloads", captured);

        var destinations = application.NavigationItems();
        Assert.NotEmpty(destinations);

        for (var i = 0; i < destinations.Length; i++)
        {
            // Re-read each time: activating a destination can rebuild the navigation, which
            // invalidates elements captured before the click.
            var items = application.NavigationItems();
            if (i >= items.Length)
            {
                break;
            }

            var destination = items[i];
            string name = destination.Properties.Name.ValueOrDefault ?? $"destination-{i}";
            RunningApplication.Activate(destination);
            Thread.Sleep(900);

            await Capture(application, directory, $"{i + 2:D2}-{Slug(name)}", captured);
        }

        Assert.NotEmpty(captured);
        foreach (string file in captured)
        {
            Assert.True(new FileInfo(file).Length > 0, $"{file} is empty");
        }
    }

    /// <summary>
    /// Fixes the window size so two runs are comparable. Best effort: a window manager is entitled
    /// to refuse, and a differently sized screenshot is still worth having.
    /// </summary>
    private static void Resize(Window window)
    {
        try
        {
            if (window.Patterns.Transform.TryGetPattern(out var transform) && transform.CanResize)
            {
                transform.Resize(Width, Height);
            }
        }
        catch (Exception)
        {
            // Left at whatever size it opened.
        }
    }

    private static async Task Capture(RunningApplication application, string directory, string name, List<string> captured)
    {
        string path = Path.Combine(directory, name + ".png");
        await application.CaptureWindowToFileAsync(path, TestContext.Current.CancellationToken);
        captured.Add(path);
    }

    private static string Slug(string value)
    {
        var characters = value.ToLower(CultureInfo.InvariantCulture)
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        return new string(characters).Trim('-').Replace("--", "-", StringComparison.Ordinal) is { Length: > 0 } slug
            ? slug
            : "destination";
    }
}
