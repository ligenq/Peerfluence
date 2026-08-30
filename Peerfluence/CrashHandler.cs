using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Peerfluence.Properties;

namespace Peerfluence;

internal static class CrashHandler
{
    private static readonly string CrashDirectory;
    private static bool HasCrashed;

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
        if (HasCrashed)
        {
            return;
        }

        HasCrashed = true;

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

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var fileName = isFatal ? $"crash_{timestamp}.log" : $"unobserved_{timestamp}.log";
        var path = Path.Combine(CrashDirectory, fileName);

        var sb = new StringBuilder();
        sb.AppendLine(isFatal ? "=== FATAL CRASH ===" : "=== UNOBSERVED TASK EXCEPTION ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Timestamp: {DateTime.Now:O}");
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
        const string script = "on run argv\n" +
            "display dialog (item 1 of argv) with title (item 2 of argv) buttons {(item 3 of argv)} default button (item 3 of argv) with icon stop\n" +
            "end run";
        var startInfo = new ProcessStartInfo
        {
            FileName = "osascript",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(message);
        startInfo.ArgumentList.Add(Resources.Crash_Title);
        startInfo.ArgumentList.Add(Resources.Common_OK);
        Process.Start(startInfo)?.WaitForExit(10_000);
    }

    private static void ShowLinuxDialog(string message)
    {
        // Try zenity first (GTK), then kdialog (KDE).
        var started = TryStartProcess("zenity", ["--error", $"--title={Resources.Crash_Title}", $"--text={message}", "--width=400"])
                   || TryStartProcess("kdialog", ["--error", message, "--title", Resources.Crash_Title]);

        // If neither is available, stderr output from the caller is the fallback.
        _ = started;
    }

    private static bool TryStartProcess(string fileName, IReadOnlyList<string> arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = Process.Start(startInfo);

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
