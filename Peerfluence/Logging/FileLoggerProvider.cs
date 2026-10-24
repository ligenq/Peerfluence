using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Peerfluence.Logging;

/// <summary>
/// Writes the profile's log, once told the profile is this process's to write.
///
/// <para>
/// Taking over the file archives the previous log and starts a fresh one, which is only right for
/// the instance that owns the profile. Nothing can know that until the single-instance lock has been
/// tried, and that comes after logging is set up - so the file is left alone until
/// <see cref="Open"/>, and anything logged before then is held and written when it is called. A
/// process that turns out to be a second launch is disposed without ever opening it, which is what
/// keeps it from emptying the log of the instance already running.
/// </para>
/// </summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private const int RetainedArchiveCount = 5;

    // Only a few lines are logged before the lock is decided. The bound is for a caller that never
    // opens the file, which must not turn into memory held for the life of the process.
    private const int MaxPendingLines = 1000;

    private readonly Lock _lock = new();
    private readonly string _path;
    private readonly List<string> _pending = [];
    private StreamWriter? _writer;
    private bool _disposed;

    public FileLoggerProvider(string path)
    {
        _path = path;
    }

    /// <summary>
    /// Archives the previous log, starts a fresh one, and writes everything logged so far.
    /// </summary>
    public void Open()
    {
        lock (_lock)
        {
            if (_disposed || _writer != null)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            ArchiveExistingLog(_path);

            var stream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };

            foreach (var line in _pending)
            {
                _writer.WriteLine(line);
            }

            _pending.Clear();
        }
    }

    private static void ArchiveExistingLog(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var archivePath = Path.Combine(
            directory ?? string.Empty,
            $"{fileName}.{timestamp}{extension}");

        try
        {
            File.Move(path, archivePath);
            PruneOldArchives(directory, fileName, extension);
        }
        catch (IOException)
        {
            // Another process may have the log open. In that case keep startup
            // logging available and let FileMode.Create truncate the active file.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void PruneOldArchives(string? directory, string fileName, string extension)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var archives = Directory
            .EnumerateFiles(directory, $"{fileName}.*{extension}", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(RetainedArchiveCount);

        foreach (var archive in archives)
        {
            try
            {
                File.Delete(archive);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _writer?.Dispose();
            _pending.Clear();
        }
    }

    private void WriteLine(string message)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            if (_writer is null)
            {
                if (_pending.Count < MaxPendingLines)
                {
                    _pending.Add(message);
                }

                return;
            }

            _writer.WriteLine(message);
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly FileLoggerProvider _provider;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception == null)
            {
                return;
            }

            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var line = $"{timestamp} [{logLevel}] {_category}: {message}";
            if (exception != null)
            {
                line += Environment.NewLine + exception;
            }

            _provider.WriteLine(line);
        }
    }
}
