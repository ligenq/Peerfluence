using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Peerfluence.Logging;

internal sealed class FileLoggerProvider : ILoggerProvider
{
    private const int RetainedArchiveCount = 5;

    private readonly Lock _lock = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public FileLoggerProvider(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        ArchiveExistingLog(path);

        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
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
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff");
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
            _writer.Dispose();
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

            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"{timestamp} [{logLevel}] {_category}: {message}";
            if (exception != null)
            {
                line += Environment.NewLine + exception;
            }

            _provider.WriteLine(line);
        }
    }
}
