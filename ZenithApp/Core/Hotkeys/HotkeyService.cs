using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using ZenithApp.Core.Logging;

namespace ZenithApp.Core.Hotkeys;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
    NoRepeat = 0x4000
}

public interface IHotkeyService : IDisposable
{
    bool RegisterHotkey(string id, KeyModifiers modifiers, Key key, Action action);
    bool UnregisterHotkey(string id);
    void UnregisterAll();
}

public sealed class HotkeyService : IHotkeyService
{
    private const int WM_HOTKEY = 0x0312;
    private readonly IAppLogger _logger;
    private readonly HwndSource _hwndSource;
    private readonly Dictionary<string, int> _idToNumericId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, (string StringId, Action Action)> _numericIdToEntry = new();
    private int _currentIdCounter = 1000;
    private bool _disposed;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public HotkeyService(IAppLogger logger)
    {
        _logger = logger;
        // Message-only window for receiving WM_HOTKEY without keeping a visible GUI window alive
        var parameters = new HwndSourceParameters("ZenithHotkeyMessageSink")
        {
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(HwndHook);
    }

    public bool RegisterHotkey(string id, KeyModifiers modifiers, Key key, Action action)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HotkeyService));

        UnregisterHotkey(id);

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            _logger.LogWarning($"Cannot register hotkey '{id}': invalid virtual key {key}.");
            return false;
        }

        var numericId = Interlocked.Increment(ref _currentIdCounter);
        var success = RegisterHotKey(_hwndSource.Handle, numericId, (uint)modifiers, (uint)virtualKey);

        if (!success)
        {
            var err = Marshal.GetLastWin32Error();
            _logger.LogWarning($"Failed to register hotkey '{id}' ({modifiers} + {key}). Win32 error code: {err} (usually hotkey conflict).");
            return false;
        }

        _idToNumericId[id] = numericId;
        _numericIdToEntry[numericId] = (id, action);
        _logger.LogInfo($"Hotkey '{id}' ({modifiers} + {key}) registered successfully.");
        return true;
    }

    public bool UnregisterHotkey(string id)
    {
        if (_idToNumericId.TryGetValue(id, out var numericId))
        {
            UnregisterHotKey(_hwndSource.Handle, numericId);
            _idToNumericId.Remove(id);
            _numericIdToEntry.Remove(numericId);
            _logger.LogInfo($"Hotkey '{id}' unregistered.");
            return true;
        }
        return false;
    }

    public void UnregisterAll()
    {
        foreach (var numericId in _numericIdToEntry.Keys)
        {
            UnregisterHotKey(_hwndSource.Handle, numericId);
        }
        _idToNumericId.Clear();
        _numericIdToEntry.Clear();
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var numericId = wParam.ToInt32();
            if (_numericIdToEntry.TryGetValue(numericId, out var entry))
            {
                try
                {
                    entry.Action.Invoke();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error executing hotkey action '{entry.StringId}'.", ex);
                }
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        _hwndSource.RemoveHook(HwndHook);
        _hwndSource.Dispose();
    }
}
