namespace ZenithApp.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public interface IAppLogger
{
    void Log(LogLevel level, string message, Exception? exception = null);
    void LogInfo(string message) => Log(LogLevel.Info, message);
    void LogWarning(string message, Exception? exception = null) => Log(LogLevel.Warning, message, exception);
    void LogError(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);
}
