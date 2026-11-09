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

    private static readonly ConcurrentDictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);

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

        // Игнорируем сам процесс Zenith
        if (pid == Environment.ProcessId) return null;

        string procName = string.Empty;
        string exePath = string.Empty;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            procName = proc.ProcessName;
            exePath = proc.MainModule?.FileName ?? string.Empty;
        }
        catch { }

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

    public static List<WindowInfo> GetOpenWindows(nint foregroundHwnd, int maxCount = 12)
    {
        var result = new List<WindowInfo>();

        EnumWindows((hWnd, _) =>
        {
            if (hWnd == foregroundHwnd) return true; // активное окно отображается отдельно в центре

            if (!IsCandidateWindow(hWnd)) return true;

            var info = GetWindowInfo(hWnd, foregroundHwnd);
            if (info != null)
            {
                result.Add(info);
                if (result.Count >= maxCount)
                {
                    return false; // Достаточно окон
                }
            }

            return true;
        }, nint.Zero);

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
        }
        catch { }
    }

    public static ImageSource? GetWindowOrProcessIcon(nint hWnd, string exePath)
    {
        // 1. Попытка получить WM_GETICON из окна
        try
        {
            if (SendMessageTimeout(hWnd, WM_GETICON, ICON_BIG, 0, 2, 80, out var hIcon) != nint.Zero && hIcon != nint.Zero)
            {
                var src = CreateBitmapSourceFromHIcon(hIcon);
                if (src != null) return src;
            }

            if (SendMessageTimeout(hWnd, WM_GETICON, ICON_SMALL, 0, 2, 80, out hIcon) != nint.Zero && hIcon != nint.Zero)
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
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            if (_iconCache.TryGetValue(exePath, out var cached))
            {
                return cached;
            }

            try
            {
                if (File.Exists(exePath))
                {
                    using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (sysIcon != null)
                    {
                        var src = CreateBitmapSourceFromHIcon(sysIcon.Handle);
                        if (src != null)
                        {
                            _iconCache[exePath] = src;
                            return src;
                        }
                    }
                }
            }
            catch { }
        }

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
