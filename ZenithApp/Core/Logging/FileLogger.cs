using System.IO;
using System.Text;

namespace ZenithApp.Core.Logging;

public sealed class FileLogger : IAppLogger, IDisposable
{
    private readonly string _logDirectory;
    private readonly string _currentLogFile;
    private readonly long _maxFileSizeBytes;
    private readonly int _maxArchiveFiles;
    private readonly object _syncLock = new();

    public FileLogger(string? customLogDir = null, long maxFileSizeBytes = 2 * 1024 * 1024, int maxArchiveFiles = 3)
    {
        _logDirectory = customLogDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ZenithApp",
            "Logs"
        );
        _maxFileSizeBytes = maxFileSizeBytes;
        _maxArchiveFiles = maxArchiveFiles;

        Directory.CreateDirectory(_logDirectory);
        _currentLogFile = Path.Combine(_logDirectory, "app.log");
    }

    public string LogDirectory => _logDirectory;

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logLine = $"[{timestamp}] [{level.ToString().ToUpperInvariant()}] {message}";
        if (exception != null)
        {
            logLine += Environment.NewLine + exception;
        }

        lock (_syncLock)
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(_currentLogFile, logLine + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Never crash the host application due to logging errors
            }
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_currentLogFile))
        {
            return;
        }

        var fileInfo = new FileInfo(_currentLogFile);
        if (fileInfo.Length < _maxFileSizeBytes)
        {
            return;
        }

        // Delete the oldest archive if present
        var oldest = Path.Combine(_logDirectory, $"app.{_maxArchiveFiles}.log");
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        // Shift archives: app.2.log -> app.3.log, app.1.log -> app.2.log
        for (var i = _maxArchiveFiles - 1; i >= 1; i--)
        {
            var source = Path.Combine(_logDirectory, $"app.{i}.log");
            var dest = Path.Combine(_logDirectory, $"app.{i + 1}.log");
            if (File.Exists(source))
            {
                File.Move(source, dest, overwrite: true);
            }
        }

        // Move current to app.1.log
        var firstArchive = Path.Combine(_logDirectory, "app.1.log");
        File.Move(_currentLogFile, firstArchive, overwrite: true);
    }

    public void Dispose()
    {
    }
}
