using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Material.Icons;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Peerfluence.Core.Messaging;
using Peerfluence.Properties;
using PeerSharp.Interfaces;

namespace Peerfluence.Services;

internal interface ICompletionActionRunner
{
    Task<CompletionActionResult> RunAsync(ITorrent torrent, CompletionActionSettings settings, CancellationToken cancellationToken);
}

internal sealed record CompletionActionResult(bool Started, int? ExitCode, string? Error);

internal sealed class TorrentCompletionActionHostedService : IHostedService
{
    private readonly IAppSettingsService _settingsService;
    private readonly ICompletionActionRunner _runner;
    private readonly INotificationService _notificationService;
    private readonly ILogger<TorrentCompletionActionHostedService> _logger;
    private readonly CancellationTokenSource _stopTokenSource = new();

    public TorrentCompletionActionHostedService(
        IAppSettingsService settingsService,
        ICompletionActionRunner runner,
        INotificationService notificationService,
        ILogger<TorrentCompletionActionHostedService> logger)
    {
        _settingsService = settingsService;
        _runner = runner;
        _notificationService = notificationService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        WeakReferenceMessenger.Default.Register<TorrentAlertMessage>(this, (_, msg) => OnTorrentAlert(msg));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopTokenSource.Cancel();
        WeakReferenceMessenger.Default.Unregister<TorrentAlertMessage>(this);
        return Task.CompletedTask;
    }

    private void OnTorrentAlert(TorrentAlertMessage msg)
    {
        if (msg.Alert.Id != AlertId.TorrentFinished)
        {
            return;
        }

        var settings = _settingsService.Current.CompletionAction;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ProgramPath))
        {
            return;
        }

        _ = RunAndNotifyAsync(msg.Torrent, settings, _stopTokenSource.Token);
    }

    private async Task RunAndNotifyAsync(ITorrent torrent, CompletionActionSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _runner.RunAsync(torrent, settings, cancellationToken).ConfigureAwait(false);
            if (!result.Started)
            {
                Publish(Resources.Notification_CompletionActionFailed, result.Error ?? torrent.Name, NotificationType.Error, MaterialIconKind.AlertCircleOutline);
                return;
            }

            if (result.ExitCode is 0)
            {
                Publish(Resources.Notification_CompletionActionFinished, torrent.Name, NotificationType.Success, MaterialIconKind.CheckCircleOutline);
            }
            else if (result.ExitCode is int exitCode)
            {
                Publish(Resources.Notification_CompletionActionFailed, string.Format(Resources.CompletionAction_ExitCode, torrent.Name, exitCode), NotificationType.Error, MaterialIconKind.AlertCircleOutline);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Completion action failed for {TorrentName}", torrent.Name);
            Publish(Resources.Notification_CompletionActionFailed, $"{torrent.Name}: {ex.Message}", NotificationType.Error, MaterialIconKind.AlertCircleOutline);
        }
    }

    private void Publish(string title, string message, Peerfluence.Core.NotificationType type, MaterialIconKind icon)
    {
        _notificationService.Publish(new NotificationItem(title, message, type, icon.ToString()), TimeSpan.FromSeconds(10));
    }
}

internal sealed class CompletionActionRunner : ICompletionActionRunner
{
    private readonly ILogger<CompletionActionRunner> _logger;

    public CompletionActionRunner(ILogger<CompletionActionRunner> logger)
    {
        _logger = logger;
    }

    public async Task<CompletionActionResult> RunAsync(ITorrent torrent, CompletionActionSettings settings, CancellationToken cancellationToken)
    {
        var programPath = ExpandTokens(settings.ProgramPath, torrent).Trim();
        if (string.IsNullOrWhiteSpace(programPath))
        {
            return new CompletionActionResult(false, null, Resources.CompletionAction_ErrorNoProgram);
        }

        if (!File.Exists(programPath))
        {
            return new CompletionActionResult(false, null, string.Format(Resources.CompletionAction_ErrorProgramNotFound, programPath));
        }

        var arguments = ExpandTokens(settings.ArgumentsTemplate, torrent);
        var workingDirectory = ExpandTokens(settings.WorkingDirectoryTemplate, torrent).Trim();
        if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory))
        {
            return new CompletionActionResult(false, null, string.Format(Resources.CompletionAction_ErrorWorkingDirectoryNotFound, workingDirectory));
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)));

        using var process = new Process
        {
            StartInfo = BuildStartInfo(programPath, arguments, workingDirectory, settings.RunHidden)
        };

        _logger.LogInformation("Starting completion action for {TorrentName}: {ProgramPath}", torrent.Name, programPath);
        if (!process.Start())
        {
            return new CompletionActionResult(false, null, Resources.CompletionAction_ErrorCouldNotStart);
        }

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new CompletionActionResult(false, null, string.Format(Resources.CompletionAction_ErrorTimedOut, settings.TimeoutSeconds));
        }

        _logger.LogInformation("Completion action for {TorrentName} exited with code {ExitCode}", torrent.Name, process.ExitCode);
        return new CompletionActionResult(true, process.ExitCode, null);
    }

    internal static string ExpandTokens(string template, ITorrent torrent)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        return template
            .Replace("{name}", torrent.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{hash}", torrent.Hash.ToHexString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{downloadPath}", torrent.Files.DownloadPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{totalSize}", torrent.TotalSize.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    private static ProcessStartInfo BuildStartInfo(string programPath, string arguments, string workingDirectory, bool runHidden)
    {
        var extension = Path.GetExtension(programPath).ToLowerInvariant();
        var isWindowsCommandScript = OperatingSystem.IsWindows()
            && (extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase));

        var startInfo = extension switch
        {
            ".bat" or ".cmd" when isWindowsCommandScript => BuildWindowsCommandScriptStartInfo(programPath, arguments),
            ".ps1" when OperatingSystem.IsWindows() => BuildPowerShellScriptStartInfo(programPath, arguments),
            ".sh" when !OperatingSystem.IsWindows() => BuildShellScriptStartInfo(programPath, arguments),
            _ => BuildExecutableStartInfo(programPath, arguments)
        };

        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = runHidden;

        if (runHidden)
        {
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        return startInfo;
    }

    private static ProcessStartInfo BuildExecutableStartInfo(string programPath, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = programPath
        };

        foreach (var argument in SplitArguments(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static ProcessStartInfo BuildWindowsCommandScriptStartInfo(string programPath, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
        };

        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        var command = new StringBuilder(QuoteForCommandShell(programPath));
        foreach (var argument in SplitArguments(arguments))
        {
            command.Append(' ');
            command.Append(QuoteForCommandShell(argument));
        }

        startInfo.ArgumentList.Add(command.ToString());
        return startInfo;
    }

    private static string QuoteForCommandShell(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal)}\"";
    }

    private static ProcessStartInfo BuildPowerShellScriptStartInfo(string programPath, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe"
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(programPath);
        foreach (var argument in SplitArguments(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static ProcessStartInfo BuildShellScriptStartInfo(string programPath, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh"
        };

        startInfo.ArgumentList.Add(programPath);
        foreach (var argument in SplitArguments(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    internal static IReadOnlyList<string> SplitArguments(string arguments)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return result;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < arguments.Length; i++)
        {
            var c = arguments[i];
            if (c == '\\' && i + 1 < arguments.Length && arguments[i + 1] == '"')
            {
                current.Append('"');
                i++;
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                AddCurrentArgument(result, current);
                continue;
            }

            current.Append(c);
        }

        AddCurrentArgument(result, current);
        return result;
    }

    private static void AddCurrentArgument(ICollection<string> arguments, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        arguments.Add(current.ToString());
        current.Clear();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup after timeout.
        }
    }
}
