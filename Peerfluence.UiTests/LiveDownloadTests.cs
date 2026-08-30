using FlaUI.Core.AutomationElements;

namespace Peerfluence.UiTests;

/// <summary>
/// The application downloading something real, driven the way a person drives it.
/// </summary>
/// <remarks>
/// <para>
/// Everything else here runs against an application with no torrents in it, which leaves the part
/// Peerfluence exists for untested end to end: resolving a magnet's metadata from the swarm,
/// connecting to peers, and showing that any of it is happening.
/// </para>
/// <para>
/// The torrent is the official Raspberry Pi OS image, from Raspberry Pi's own tracker. It is a
/// large file, so this never downloads it: a speed limit is written into the profile before the
/// application starts, the test stops the torrent as soon as it has seen what it came to see, and
/// the profile - downloads included - is deleted when the test ends.
/// </para>
/// <para>
/// Off by default, because it needs the internet, other people's machines and a minute of both.
/// It reports as skipped rather than passing quietly:
/// </para>
/// <code>
/// $env:PEERFLUENCE_LIVE_DOWNLOAD = "1"
/// dotnet test --project Peerfluence.UiTests/Peerfluence.UiTests.csproj
/// </code>
/// </remarks>
public sealed class LiveDownloadTests
{
    private const string RaspberryPiOsMagnet =
        "magnet:?xt=urn:btih:G23OG4HHW32UKFL34G4NHACQZGBUQHZC"
        + "&dn=2026-06-18-raspios-trixie-arm64.img.xz"
        + "&xl=1344722248"
        + "&tr=http%3A%2F%2Ftracker.raspberrypi.org%3A6969%2Fannounce";

    /// <summary>Enough of the file name to recognise it once the metadata has arrived.</summary>
    private const string ExpectedName = "raspios";

    /// <summary>
    /// A profile that has already chosen an interface, will not stop to ask about the add, and is
    /// capped at a quarter of a megabyte a second.
    /// </summary>
    /// <remarks>
    /// The cap is what makes this safe to run on somebody's connection. The test asks the swarm for
    /// a gigabyte and a third and takes a few seconds of it.
    /// </remarks>
    private const string Settings =
        """
        {
          "InterfaceMode": "Advanced",
          "ShowAddTorrentOptions": false,
          "Network": { "MaxDownloadSpeedBytesPerSecond": 262144 }
        }
        """;

    private static void SkipUnlessAsked() =>
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("PEERFLUENCE_LIVE_DOWNLOAD") == "1",
            "Set PEERFLUENCE_LIVE_DOWNLOAD=1 to run the tests that download something real.");

    /// <summary>
    /// One scenario rather than several tests, deliberately: each one would start the application
    /// and ask the swarm for the same torrent again, and the interesting thing is the sequence.
    /// </summary>
    [Fact]
    public void AMagnet_ResolvesItsMetadataAndStartsDownloadingFromRealPeers()
    {
        SkipUnlessAsked();

        using var app = new RunningApplication(Settings);
        RunningApplication.Until(() => app.Exists("AddMagnetButton"), "the downloads screen");

        PutOnTheClipboard(RaspberryPiOsMagnet);
        app.Find("AddMagnetButton").AsButton().Invoke();

        // 1. It is in the list. The name is the one from the magnet until metadata says otherwise.
        RunningApplication.Until(
            () => app.ShowsText(ExpectedName),
            "the torrent to appear in the list",
            withinSeconds: 30);

        // 2. The engine knows about it. This is the first thing that needs the outside world: the
        //    torrent is counted only once the engine has taken it.
        app.GoTo("StatisticsPage");
        RunningApplication.Until(
            () => app.Text("StatsTorrentsValue") == "1",
            $"the engine to report one torrent, got '{app.Text("StatsTorrentsValue")}'",
            withinSeconds: 30);

        // 3. Peers. A magnet has no file list until somebody sends one, so connecting is what has to
        //    happen before anything else can.
        RunningApplication.Until(
            () => Number(app.Text("StatsConnectedPeersValue")) > 0,
            "at least one peer to connect",
            withinSeconds: 120);

        // 4. Bytes. The point of the whole application.
        RunningApplication.Until(
            () => app.Text("StatsDownloadedValue") is { } downloaded && downloaded != "0 B",
            "something to actually arrive",
            withinSeconds: 180);

        // Stopped from the interface, before it has taken any real amount of the file or of
        // somebody's bandwidth.
        app.GoTo("AddMagnetButton");
        app.Find("PauseAllButton").AsButton().Invoke();

        app.GoTo("StatisticsPage");
        RunningApplication.Until(
            () => app.Text("StatsActiveTorrentsValue") == "0",
            $"the torrent to stop, still reads '{app.Text("StatsActiveTorrentsValue")}' active",
            withinSeconds: 60);
    }

    private static long Number(string? text) =>
        long.TryParse(text, out var value) ? value : 0;

    /// <summary>
    /// Adding a magnet reads the clipboard first, and adds it directly when what it finds is one.
    /// </summary>
    private static void PutOnTheClipboard(string value)
    {
        using var powershell = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell")
        {
            ArgumentList = { "-NoProfile", "-Command", $"Set-Clipboard -Value '{value}'" },
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        powershell?.WaitForExit(10_000);
    }
}
