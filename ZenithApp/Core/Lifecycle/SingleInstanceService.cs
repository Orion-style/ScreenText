using System.IO;
using System.IO.Pipes;
using System.Text;
using ZenithApp.Core.Logging;

namespace ZenithApp.Core.Lifecycle;

public interface ISingleInstanceService : IDisposable
{
    bool IsFirstInstance { get; }
    void StartListening(Action onSecondInstanceLaunched);
    void NotifyFirstInstance();
}

public sealed class SingleInstanceService : ISingleInstanceService
{
    private const string PipeName = "ZenithApp_SingleInstance_Pipe_9b4e2874";
    private readonly string _mutexName;
    private readonly IAppLogger _logger;
    private Mutex? _mutex;
    private bool _isFirstInstance;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public SingleInstanceService(string appName, IAppLogger logger)
    {
        _logger = logger;
        // Use Local\ for single session desktop instances without requiring elevated SeCreateGlobalPrivilege
        _mutexName = $"Local\\{appName}_SingleInstance_Mutex_9b4e2874";

        try
        {
            _mutex = new Mutex(true, _mutexName, out _isFirstInstance);
            if (!_isFirstInstance)
            {
                _logger.LogInfo($"Existing mutex '{_mutexName}' detected: application is already running.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to create single instance Mutex.", ex);
            _isFirstInstance = true;
        }
    }

    public bool IsFirstInstance => _isFirstInstance;

    public void StartListening(Action onSecondInstanceLaunched)
    {
        if (!_isFirstInstance) return;

        _cts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(_cts.Token);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var message = await reader.ReadToEndAsync();
                    if (message.Trim() == "ACTIVATE")
                    {
                        _logger.LogInfo("Received activation signal from second instance.");
                        onSecondInstanceLaunched.Invoke();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Error in single-instance pipe listener.", ex);
                    await Task.Delay(500);
                }
            }
        }, _cts.Token);
    }

    public void NotifyFirstInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000); // 2 sec timeout
            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.Write("ACTIVATE");
            writer.Flush();
            _logger.LogInfo("Notified first instance successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to notify running instance via pipe: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();

        if (_mutex != null)
        {
            if (_isFirstInstance)
            {
                try { _mutex.ReleaseMutex(); } catch { }
            }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
