using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Peerfluence.Core.Messaging;

namespace Peerfluence.Services;

public sealed class SingleInstanceService : ISingleInstanceService, IDisposable
{
    // A profile-scoped lock file. Holding an exclusive handle to it marks this
    // process as the single instance. FileShare.None maps to native exclusive
    // locking on Windows and an advisory flock() on Unix, and the OS releases the
    // lock automatically if the process dies — so there is no stale-lock problem.
    private readonly ILogger<SingleInstanceService> _logger;
    private readonly string _pipeName;
    private readonly string _lockFilePath;
    private FileStream? _lockFile;
    private bool _hasHandle;
    private CancellationTokenSource? _listenerCts;

    public SingleInstanceService(ILogger<SingleInstanceService> logger, IAppPaths appPaths)
    {
        _logger = logger;
        _pipeName = ProfileIpcNames.GetSingleInstancePipeName(appPaths);
        _lockFilePath = ProfileIpcNames.GetLockFilePath(appPaths);
    }

    public bool TryAcquireSingleInstanceLock()
    {
        if (_lockFile != null)
        {
            return _hasHandle;
        }

        try
        {
            _lockFile = new FileStream(
                _lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            _hasHandle = true;
            _logger.LogInformation("Single instance lock acquired.");
            return true;
        }
        catch (IOException)
        {
            // The lock file is held exclusively by another instance.
            _hasHandle = false;
            _logger.LogInformation("Another instance is already running. Single instance lock failed.");
            return false;
        }
        catch (Exception ex)
        {
            // The locking mechanism itself is unavailable (e.g. a permissions
            // problem on the lock file). We cannot tell whether another instance is
            // running, so fail open and launch as the sole instance rather than
            // silently refusing to start.
            _hasHandle = false;
            _logger.LogError(ex, "Error checking for single instance. Proceeding as sole instance.");
            return true;
        }
    }

    public void StartListening()
    {
        _listenerCts = new CancellationTokenSource();
        Task.Run(() => ListenForActivationAsync(_listenerCts.Token));
    }

    public void SignalExistingInstance(IReadOnlyList<string>? arguments = null)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(1000);
            using var writer = new StreamWriter(client);
            foreach (var argument in arguments ?? Array.Empty<string>())
            {
                writer.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(argument)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to signal existing instance");
        }
    }

    public void ReleaseLock()
    {
        _listenerCts?.Cancel();
        _listenerCts?.Dispose();
        _listenerCts = null;

        if (_hasHandle && _lockFile != null)
        {
            try
            {
                _lockFile.Dispose();
                _lockFile = null;
                _hasHandle = false;
                _logger.LogInformation("Single instance lock released.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error releasing single instance lock.");
            }
        }
    }

    public void Dispose()
    {
        ReleaseLock();
        _lockFile?.Dispose();
        _lockFile = null;
    }

    private async Task ListenForActivationAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);
                _logger.LogInformation("Activation signal received from another instance.");
                var arguments = await ReadActivationArgumentsAsync(server, ct);
                WeakReferenceMessenger.Default.Send(new ActivationRequestedMessage(arguments));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in single-instance listener");
            }
        }
    }

    private static async Task<IReadOnlyList<string>> ReadActivationArgumentsAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream);
        var arguments = new List<string>();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                arguments.Add(Encoding.UTF8.GetString(Convert.FromBase64String(line)));
            }
            catch (FormatException)
            {
            }
        }

        return arguments;
    }
}
