using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Peerfluence.Core.Messaging;

namespace Peerfluence.Services;

public sealed class SingleInstanceService : ISingleInstanceService, IDisposable
{
    private const string MutexId = @"Global\Peerfluence-2C7F3A9D-5B8E-4F1A-9C3D-7E6F8A2B4C5D";
    private const string PipeName = "Peerfluence-SingleInstance-2C7F3A9D";

    private readonly ILogger<SingleInstanceService> _logger;
    private Mutex? _mutex;
    private bool _hasHandle;
    private CancellationTokenSource? _listenerCts;

    public SingleInstanceService(ILogger<SingleInstanceService> logger)
    {
        _logger = logger;
    }

    public bool TryAcquireSingleInstanceLock()
    {
        if (_mutex != null)
        {
            return _hasHandle;
        }

        try
        {
            _mutex = new Mutex(false, MutexId);

            try
            {
                _hasHandle = _mutex.WaitOne(0, false);
            }
            catch (AbandonedMutexException)
            {
                _hasHandle = true;
                _logger.LogWarning("SingleInstance mutex was abandoned by a previous instance.");
            }

            if (_hasHandle)
            {
                _logger.LogInformation("Single instance lock acquired.");
            }
            else
            {
                _logger.LogInformation("Another instance is already running. Single instance lock failed.");
            }

            return _hasHandle;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for single instance.");
            return false;
        }
    }

    public void StartListening()
    {
        _listenerCts = new CancellationTokenSource();
        Task.Run(() => ListenForActivationAsync(_listenerCts.Token));
    }

    public void SignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1000);
            client.WriteByte(1);
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

        if (_hasHandle && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
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
        _mutex?.Dispose();
        _mutex = null;
    }

    private async Task ListenForActivationAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);
                _logger.LogInformation("Activation signal received from another instance.");
                WeakReferenceMessenger.Default.Send(new ActivationRequestedMessage());
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
}

