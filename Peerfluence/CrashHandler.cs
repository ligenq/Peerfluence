using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Peerfluence.Properties;

namespace Peerfluence;

internal static class CrashHandler
{
    private static readonly string CrashDirectory;
    private static bool _hasCrashed;

    static CrashHandler()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = AppContext.BaseDirectory;
        }

        CrashDirectory = Path.Combine(appData, "Peerfluence");
    }

    public static void Register()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public static void HandleException(Exception exception)
    {
        if (_hasCrashed)
        {
            return;
        }

        _hasCrashed = true;

        string? crashLogPath = null;
        try
        {
            crashLogPath = WriteCrashLog(exception);
        }
        catch
        {
            // If we can't write the crash log, still try to show the message.
        }

        ShowCrashMessage(crashLogPath, exception);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            HandleException(ex);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Don't terminate for unobserved task exceptions — just log them.
        e.SetObserved();

        try
        {
            WriteCrashLog(e.Exception, isFatal: false);
        }
        catch
        {
            // Best-effort logging.
        }
    }

    private static string? WriteCrashLog(Exception exception, bool isFatal = true)
    {
        Directory.CreateDirectory(CrashDirectory);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = isFatal ? $"crash_{timestamp}.log" : $"unobserved_{timestamp}.log";
        var path = Path.Combine(CrashDirectory, fileName);

        var sb = new StringBuilder();
        sb.AppendLine(isFatal ? "=== FATAL CRASH ===" : "=== UNOBSERVED TASK EXCEPTION ===");
        sb.AppendLine($"Timestamp: {DateTime.Now:O}");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine();
        sb.AppendLine("--- Exception ---");
        sb.AppendLine(exception.ToString());
        sb.AppendLine();

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.Flatten().InnerExceptions)
            {
                sb.AppendLine("--- Inner Exception ---");
                sb.AppendLine(inner.ToString());
                sb.AppendLine();
            }
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return path;
    }

    private static void ShowCrashMessage(string? crashLogPath, Exception exception)
    {
        var message = Resources.Crash_UnexpectedError;
        if (crashLogPath != null)
        {
            message += $"\n\n{Resources.Crash_ReportSaved}\n{crashLogPath}";
        }

        message += $"\n\n{exception.GetType().Name}: {exception.Message}";

        // Always write to stderr as a baseline.
        Console.Error.WriteLine(message);

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ShowWindowsMessageBox(message);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                ShowMacOsDialog(message);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                ShowLinuxDialog(message);
            }
        }
        catch
        {
            // stderr output above is the fallback.
        }
    }

    private static void ShowWindowsMessageBox(string message)
    {
        _ = MessageBoxW(IntPtr.Zero, message, Resources.Crash_Title, 0x10 /* MB_ICONERROR */);
    }

    private static void ShowMacOsDialog(string message)
    {
        var escaped = message.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var escapedTitle = Resources.Crash_Title.Replace("\\", "\\\\").Replace("\"", "\\\"");
        Process.Start(new ProcessStartInfo
        {
            FileName = "osascript",
            Arguments = $"-e 'display dialog \"{escaped}\" with title \"{escapedTitle}\" buttons {{\"{Resources.Common_OK}\"}} default button \"{Resources.Common_OK}\" with icon stop'",
            UseShellExecute = false,
            CreateNoWindow = true
        })?.WaitForExit(10_000);
    }

    private static void ShowLinuxDialog(string message)
    {
        var escaped = message.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var escapedTitle = Resources.Crash_Title.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // Try zenity first (GTK), then kdialog (KDE).
        var started = TryStartProcess("zenity", $"--error --title=\"{escapedTitle}\" --text=\"{escaped}\" --width=400")
                   || TryStartProcess("kdialog", $"--error \"{escaped}\" --title \"{escapedTitle}\"");

        // If neither is available, stderr output from the caller is the fallback.
        _ = started;
    }

    private static bool TryStartProcess(string fileName, string arguments)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            process?.WaitForExit(10_000);
            return process != null;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
