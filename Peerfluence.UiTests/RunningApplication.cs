using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace Peerfluence.UiTests;

/// <summary>
/// A real Peerfluence process, started against a throwaway profile and driven through UI Automation.
/// </summary>
/// <remarks>
/// <para>
/// Two arguments make this safe to run on a machine that also uses the application. <c>--profile</c>
/// points <see cref="Peerfluence.Core.Services.AppPaths"/> at a temporary directory, so the settings,
/// the session and the downloads of the person running the tests are neither read nor written.
/// <c>--ui-agent</c> releases the single-instance lock, so a copy already open on the desktop is not
/// signalled and brought to the front instead of a new one starting.
/// </para>
/// <para>
/// One process per test rather than one shared between them. It costs a few seconds each, and buys
/// a genuinely fresh profile: the first-run behaviour is only observable on a first run, and a test
/// that depended on running before another would eventually be run after it.
/// </para>
/// </remarks>
public sealed class RunningApplication : IDisposable
{
    private readonly FlaUI.Core.Application _application;
    private readonly UIA3Automation _automation;

    private bool _ownsProfile = true;
    private bool _disposed;

    public RunningApplication()
        : this(Path.Combine(Path.GetTempPath(), "peerfluence-uitests", Guid.NewGuid().ToString("n")))
    {
    }

    private RunningApplication(string profileDirectory)
    {
        ProfileDirectory = profileDirectory;
        Directory.CreateDirectory(ProfileDirectory);

        var start = new ProcessStartInfo(ExecutablePath())
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(ExecutablePath())!,
        };
        start.ArgumentList.Add("--ui-agent");
        start.ArgumentList.Add("--profile");
        start.ArgumentList.Add(ProfileDirectory);

        _application = FlaUI.Core.Application.Launch(start);
        _automation = new UIA3Automation();

        Window = _application.GetMainWindow(_automation, TimeSpan.FromSeconds(60))
            ?? throw new InvalidOperationException("the application started but showed no window");
    }

    public Window Window { get; }

    /// <summary>The throwaway directory this instance keeps everything in.</summary>
    public string ProfileDirectory { get; }

    /// <summary>
    /// Stops the application and starts it again on the same profile.
    /// </summary>
    /// <remarks>
    /// The only way to ask whether something was actually saved. A view model can be inspected for
    /// what it holds and a file can be read for what it contains, but neither answers whether the
    /// application reads back what it wrote, which is the part that breaks.
    /// </remarks>
    public RunningApplication Restart()
    {
        _ownsProfile = false;
        Dispose();
        return new RunningApplication(ProfileDirectory);
    }

    /// <summary>The modal windows this one has opened.</summary>
    public Window[] ModalWindows => Window.ModalWindows;

    /// <summary>
    /// The element carrying <paramref name="automationId"/>, once it exists.
    /// </summary>
    /// <remarks>
    /// Polled rather than found once. A window that has been handed over is not necessarily a window
    /// that has finished laying itself out, and a dialog appears some frames after the click that
    /// asked for it.
    /// </remarks>
    public AutomationElement Find(string automationId, int withinSeconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(withinSeconds);

        do
        {
            var found = Window.FindFirstDescendant(by => by.ByAutomationId(automationId));
            if (found is not null)
            {
                return found;
            }

            Thread.Sleep(200);
        }
        while (DateTime.UtcNow < deadline);

        throw new InvalidOperationException(
            $"no element with the automation id '{automationId}' appeared within {withinSeconds}s. "
                + $"What was there: {Environment.NewLine}{Describe()}");
    }

    /// <summary>
    /// The side menu destinations, in the order they are shown.
    /// </summary>
    /// <remarks>
    /// Found by control type rather than by an id: they come from a template, so one id would name
    /// every one of them. Position is the only stable handle, and it is the same in every language.
    /// </remarks>
    public AutomationElement[] NavigationItems() =>
        Window.FindAllDescendants(by => by.ByControlType(FlaUI.Core.Definitions.ControlType.TreeItem));

    /// <summary>Selects an element, by the pattern it offers rather than by aiming the mouse at it.</summary>
    public void Activate(AutomationElement element)
    {
        if (element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
            return;
        }

        element.Click();
    }

    /// <summary>
    /// Navigates to the settings destination, whichever position it occupies.
    /// </summary>
    public void GoToSettings()
    {
        foreach (var destination in NavigationItems())
        {
            Activate(destination);

            if (Exists("AppearanceTab"))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"no navigation destination led to the settings. What was there:{Environment.NewLine}{Describe()}");
    }

    public bool Exists(string automationId) =>
        Window.FindFirstDescendant(by => by.ByAutomationId(automationId)) is not null;

    /// <summary>Waits for a condition that a click is expected to bring about.</summary>
    public static void Until(Func<bool> condition, string what, int withinSeconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(withinSeconds);

        do
        {
            if (condition())
            {
                return;
            }

            Thread.Sleep(200);
        }
        while (DateTime.UtcNow < deadline);

        throw new InvalidOperationException($"timed out waiting for {what}");
    }

    /// <summary>Every element in the window, for when a lookup fails and the reason is not obvious.</summary>
    public string Describe()
    {
        // ValueOrDefault throughout: the native title bar supports neither property, and reading one
        // that an element does not have throws rather than returning nothing.
        var lines = Window.FindAllDescendants().Select(element =>
            $"  {element.ControlType,-14} id='{element.Properties.AutomationId.ValueOrDefault}' "
                + $"name='{element.Properties.Name.ValueOrDefault}'");

        return string.Join(Environment.NewLine, lines);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Kill rather than Close: a graceful close waits for the application to agree, and a test
        // that has just left a modal dialog open would hang here rather than fail.
        try { _application.Kill(); } catch (Exception) { /* already gone */ }

        _application.Dispose();
        _automation.Dispose();

        if (!_ownsProfile)
        {
            // Handed to the instance that restarted onto it, which will clear it up.
            return;
        }

        try { Directory.Delete(ProfileDirectory, recursive: true); }
        catch (Exception) { /* a log file still held open; the temp directory is disposable */ }
    }

    /// <summary>
    /// The built application, from the same configuration these tests were built in.
    /// </summary>
    private static string ExecutablePath()
    {
        var configuration = Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar)))!;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "Peerfluence", "bin", configuration, "net10.0", "Peerfluence.exe");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Peerfluence.exe was not found in a '{configuration}' build above {AppContext.BaseDirectory}. "
                + "Build the application first: dotnet build Peerfluence/Peerfluence.csproj");
    }
}
