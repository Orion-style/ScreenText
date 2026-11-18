using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZenithApp.Modules.RadialSwitcher;

public static class WindowListService
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int DWMWA_CLOAKED = 14;

    private const int WM_GETICON = 0x007F;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const int GCLP_HICON = -14;

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
    private static extern nint GetClassLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetClassLong")]
    private static extern int GetClassLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern nint SendMessageTimeout(nint hWnd, uint msg, nint wParam, nint lParam, uint fuFlags, uint uTimeout, out nint lpdwResult);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(nint hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private static readonly ConcurrentDictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    // --- WinEvent Hook для отслеживания активации окон (MRU-приоритет) ---
    private delegate void WinEventDelegate(
        nint hWinEventHook, uint eventType, nint hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hWinEventHook);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const int OBJID_WINDOW = 0;

    private static nint _winEventHook = nint.Zero;
    private static WinEventDelegate? _winEventProc;
    private static readonly List<nint> _mruHwnds = new();
    private static readonly object _mruLock = new();

    public static void StartTracking()
    {
        if (_winEventHook != nint.Zero) return;

        _winEventProc = (hHook, eventType, hWnd, idObject, idChild, thread, time) =>
        {
            if (idObject == OBJID_WINDOW && hWnd != nint.Zero)
            {
                RecordActiveWindow(hWnd);
            }
        };

        _winEventHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND,
            EVENT_SYSTEM_FOREGROUND,
            nint.Zero,
            _winEventProc,
            0,
            0,
            WINEVENT_OUTOFCONTEXT);
    }

    public static void StopTracking()
    {
        if (_winEventHook != nint.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = nint.Zero;
            _winEventProc = null;
        }
    }

    public static void RecordActiveWindow(nint hWnd)
    {
        if (hWnd == nint.Zero) return;

        lock (_mruLock)
        {
            _mruHwnds.Remove(hWnd);
            _mruHwnds.Insert(0, hWnd);

            if (_mruHwnds.Count > 100)
            {
                _mruHwnds.RemoveRange(100, _mruHwnds.Count - 100);
            }
        }
    }

    public static void ClearMruHistory()
    {
        lock (_mruLock)
        {
            _mruHwnds.Clear();
        }
    }

    public static nint GetWindowLongPtr(nint hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
    }

    public static nint GetClassLongPtr(nint hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetClassLongPtr64(hWnd, nIndex) : GetClassLong32(hWnd, nIndex);
    }

    public static WindowInfo? GetWindowInfo(nint hWnd, nint foregroundHwnd)
    {
        if (hWnd == nint.Zero || !IsWindowVisible(hWnd)) return null;

        var length = GetWindowTextLength(hWnd);
        if (length == 0) return null;

        var sb = new StringBuilder(length + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        var title = sb.ToString().Trim();
        if (string.IsNullOrWhiteSpace(title)) return null;

        GetWindowThreadProcessId(hWnd, out var pid);
        if (pid == 0) return null;

        // Для самого процесса Zenith разрешаем отображение его главного окна,
        // но игнорируем служебные оверлеи и само радиальное меню
        if (pid == Environment.ProcessId)
        {
            if (string.IsNullOrWhiteSpace(title) || 
                title.Contains("RadialWheel", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("QuickLook", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        var (procName, exePath) = GetProcessDetails(pid);
        if (string.IsNullOrWhiteSpace(procName)) return null;

        // Фильтрация системных окон
        if (procName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
        {
            if (title == "Program Manager" || title == "Панель задач") return null;
        }

        var icon = GetWindowOrProcessIcon(hWnd, exePath);

        return new WindowInfo
        {
            Hwnd = hWnd,
            Title = title,
            ProcessName = procName,
            ExePath = exePath,
            Icon = icon,
            IsActive = hWnd == foregroundHwnd
        };
    }

    public static List<WindowInfo> GetOpenWindows(nint foregroundHwnd, int maxCount = 16)
    {
        var candidatesMap = new Dictionary<nint, WindowInfo>();
        var zOrderHwnds = new List<nint>();

        EnumWindows((hWnd, _) =>
        {
            if (hWnd == foregroundHwnd) return true; // активное окно отображается отдельно в центре

            if (!IsCandidateWindow(hWnd)) return true;

            var info = GetWindowInfo(hWnd, foregroundHwnd);
            if (info != null)
            {
                candidatesMap[hWnd] = info;
                zOrderHwnds.Add(hWnd);
            }

            return true;
        }, nint.Zero);

        var result = new List<WindowInfo>();
        var seen = new HashSet<nint>();

        // 1. Приоритет MRU: окна, с которыми пользователь взаимодействовал недавно
        lock (_mruLock)
        {
            // Очищаем историю от закрытых окон
            _mruHwnds.RemoveAll(h => !candidatesMap.ContainsKey(h) && h != foregroundHwnd);

            foreach (var hwnd in _mruHwnds)
            {
                if (hwnd == foregroundHwnd) continue;
                if (candidatesMap.TryGetValue(hwnd, out var info) && seen.Add(hwnd))
                {
                    result.Add(info);
                    if (result.Count >= maxCount) break;
                }
            }
        }

        // 2. Дополняем остальными окнами по Z-order, если слоты еще свободны
        foreach (var hwnd in zOrderHwnds)
        {
            if (result.Count >= maxCount) break;
            if (seen.Add(hwnd) && candidatesMap.TryGetValue(hwnd, out var info))
            {
                result.Add(info);
            }
        }

        return result;
    }

    public static bool IsCandidateWindow(nint hWnd)
    {
        if (!IsWindowVisible(hWnd)) return false;

        // Проверка DWM Cloaked (скрытые окна UWP/Виртуальных столов)
        if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        var exStyle = (long)GetWindowLongPtr(hWnd, GWL_EXSTYLE);
        if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0)
        {
            return false;
        }

        return true;
    }

    public static void ActivateWindow(nint hWnd)
    {
        if (hWnd == nint.Zero) return;

        try
        {
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }
            else
            {
                ShowWindow(hWnd, SW_SHOW);
            }

            SetForegroundWindow(hWnd);
            RecordActiveWindow(hWnd);
        }
        catch { }
    }

    private static (string procName, string exePath) GetProcessDetails(uint pid)
    {
        nint hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess != nint.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                {
                    string path = sb.ToString();
                    string name = Path.GetFileNameWithoutExtension(path);
                    return (name, path);
                }
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return (proc.ProcessName, proc.MainModule?.FileName ?? string.Empty);
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }

    public static ImageSource? GetWindowOrProcessIcon(nint hWnd, string exePath)
    {
        // 1. Попытка получить WM_GETICON из окна
        try
        {
            if (SendMessageTimeout(hWnd, WM_GETICON, ICON_BIG, 0, 2, 20, out var hIcon) != nint.Zero && hIcon != nint.Zero)
            {
                var src = CreateBitmapSourceFromHIcon(hIcon);
                if (src != null) return src;
            }

            if (SendMessageTimeout(hWnd, WM_GETICON, ICON_SMALL, 0, 2, 20, out hIcon) != nint.Zero && hIcon != nint.Zero)
            {
                var src = CreateBitmapSourceFromHIcon(hIcon);
                if (src != null) return src;
            }

            // GCLP_HICON
            var classIcon = GetClassLongPtr(hWnd, GCLP_HICON);
            if (classIcon != nint.Zero)
            {
                var src = CreateBitmapSourceFromHIcon(classIcon);
                if (src != null) return src;
            }
        }
        catch { }

        // 2. Иконка из исполняемого файла приложения
        return GetIconForPath(exePath);
    }

    public static ImageSource? GetIconForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (_iconCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        try
        {
            var icon = StartupManager.StartupIconService.GetIcon(path, null);
            if (icon != null)
            {
                _iconCache[path] = icon;
                return icon;
            }
        }
        catch { }

        return null;
    }

    public static ImageSource? GetIconForProcessName(string? procName)
    {
        if (string.IsNullOrWhiteSpace(procName)) return null;

        try
        {
            var procs = Process.GetProcessesByName(procName);
            foreach (var p in procs)
            {
                try
                {
                    string path = p.MainModule?.FileName ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        var icon = GetIconForPath(path);
                        if (icon != null) return icon;
                    }
                }
                catch { }
            }
        }
        catch { }

        return null;
    }

    private static ImageSource? CreateBitmapSourceFromHIcon(nint hIcon)
    {
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
    }
}
