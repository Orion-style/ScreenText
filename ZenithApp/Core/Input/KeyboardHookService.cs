using System.Runtime.InteropServices;

namespace ZenithApp.Core.Input;

public sealed class KeyboardHookEventArgs : EventArgs
{
    public int VirtualCode { get; }
    public int ScanCode { get; }
    public bool IsKeyDown { get; }
    public bool IsKeyUp { get; }
    public bool IsExtendedKey { get; }
    public bool IsInjected { get; }
    public nuint ExtraInfo { get; }

    /// <summary>
    /// If set to true by any subscriber, the key press is suppressed from reaching other applications.
    /// </summary>
    public bool SuppressKeyPress { get; set; }

    public KeyboardHookEventArgs(int virtualCode, int scanCode, bool isKeyDown, bool isKeyUp, bool isExtendedKey, bool isInjected, nuint extraInfo)
    {
        VirtualCode = virtualCode;
        ScanCode = scanCode;
        IsKeyDown = isKeyDown;
        IsKeyUp = isKeyUp;
        IsExtendedKey = isExtendedKey;
        IsInjected = isInjected;
        ExtraInfo = extraInfo;
    }
}

public interface IKeyboardHookService : IDisposable
{
    /// <summary>
    /// Registers a subscriber callback. The underlying WH_KEYBOARD_LL hook is installed
    /// when the first subscriber connects. Returns a subscription token for unregistering.
    /// </summary>
    IDisposable Subscribe(Func<KeyboardHookEventArgs, bool> handler);

    /// <summary>
    /// Returns true if the hook is currently active in Windows.
    /// </summary>
    bool IsHookActive { get; }
}

public sealed class KeyboardHookService : IKeyboardHookService
{
    public const nuint ExtraInfoAppMarker = 0x5A454E54; // "ZENT" in ASCII

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int LLKHF_EXTENDED = 0x0001;
    private const int LLKHF_INJECTED = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    private readonly object _lock = new();
    private readonly List<Func<KeyboardHookEventArgs, bool>> _subscribers = new();
    private LowLevelKeyboardProc? _hookProc;
    private nint _hookId = nint.Zero;
    private bool _disposed;

    public bool IsHookActive
    {
        get
        {
            lock (_lock)
            {
                return _hookId != nint.Zero;
            }
        }
    }

    public IDisposable Subscribe(Func<KeyboardHookEventArgs, bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_lock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(KeyboardHookService));

            _subscribers.Add(handler);
            EnsureHookInstalledLocked();
            return new SubscriptionToken(this, handler);
        }
    }

    private void Unsubscribe(Func<KeyboardHookEventArgs, bool> handler)
    {
        lock (_lock)
        {
            if (_disposed) return;

            _subscribers.Remove(handler);
            if (_subscribers.Count == 0)
            {
                EnsureHookRemovedLocked();
            }
        }
    }

    private void EnsureHookInstalledLocked()
    {
        if (_hookId != nint.Zero) return;

        // Keep a strong reference to delegate so GC does not collect it
        _hookProc = HookCallback;
        var hMod = GetModuleHandle(null);
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, hMod, 0);
    }

    private void EnsureHookRemovedLocked()
    {
        if (_hookId != nint.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = nint.Zero;
            _hookProc = null;
        }
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // Ignore synthetic injected keystrokes with our own tag or marked as injected by Windows
            // to completely avoid loops when re-emitting keys
            var isInjected = (hookStruct.flags & LLKHF_INJECTED) != 0 || hookStruct.dwExtraInfo == ExtraInfoAppMarker;

            var msg = (int)wParam;
            var isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            var isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                var isExtended = (hookStruct.flags & LLKHF_EXTENDED) != 0;
                var args = new KeyboardHookEventArgs(
                    (int)hookStruct.vkCode,
                    (int)hookStruct.scanCode,
                    isKeyDown,
                    isKeyUp,
                    isExtended,
                    isInjected,
                    hookStruct.dwExtraInfo);

                Func<KeyboardHookEventArgs, bool>[] localSubs;
                lock (_lock)
                {
                    localSubs = _subscribers.ToArray();
                }

                for (int i = 0; i < localSubs.Length; i++)
                {
                    try
                    {
                        var handled = localSubs[i](args);
                        if (handled || args.SuppressKeyPress)
                        {
                            return 1; // Suppress key event
                        }
                    }
                    catch
                    {
                        // Callback must never throw out of the native hook
                    }
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _subscribers.Clear();
            EnsureHookRemovedLocked();
        }
    }

    private sealed class SubscriptionToken : IDisposable
    {
        private KeyboardHookService? _service;
        private Func<KeyboardHookEventArgs, bool>? _handler;

        public SubscriptionToken(KeyboardHookService service, Func<KeyboardHookEventArgs, bool> handler)
        {
            _service = service;
            _handler = handler;
        }

        public void Dispose()
        {
            var svc = Interlocked.Exchange(ref _service, null);
            var h = Interlocked.Exchange(ref _handler, null);
            if (svc != null && h != null)
            {
                svc.Unsubscribe(h);
            }
        }
    }
}
