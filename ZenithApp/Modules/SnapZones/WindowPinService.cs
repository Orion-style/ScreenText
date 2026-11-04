using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace ZenithApp.Modules.SnapZones;

public static class WindowPinService
{
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOPMOST = 0x00000008L;

    private static readonly nint HWND_TOPMOST = new nint(-1);
    private static readonly nint HWND_NOTOPMOST = new nint(-2);

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint hWnd);

    public static long GetWindowExStyle(nint hWnd)
    {
        if (nint.Size == 8)
            return (long)GetWindowLongPtr64(hWnd, GWL_EXSTYLE);
        return GetWindowLong32(hWnd, GWL_EXSTYLE);
    }

    public static bool IsWindowTopmost(nint hWnd)
    {
        if (hWnd == nint.Zero) return false;
        long exStyle = GetWindowExStyle(hWnd);
        return (exStyle & WS_EX_TOPMOST) != 0;
    }

    public static bool SetWindowTopmost(nint hWnd, bool topmost)
    {
        if (hWnd == nint.Zero) return false;
        nint insertAfter = topmost ? HWND_TOPMOST : HWND_NOTOPMOST;
        return SetWindowPos(hWnd, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public static string GetProcessName(nint hWnd)
    {
        GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid == 0) return string.Empty;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string GetWindowClassName(nint hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, 256);
        return sb.ToString();
    }

    public static bool IsPinnableWindow(nint hWnd, IEnumerable<string> excludedProcesses)
    {
        if (hWnd == nint.Zero || !IsWindow(hWnd) || !IsWindowVisible(hWnd)) return false;

        string cls = GetWindowClassName(hWnd);
        if (cls.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
            cls.Equals("WorkerW", StringComparison.OrdinalIgnoreCase) ||
            cls.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
            cls.Equals("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase) ||
            cls.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string procName = GetProcessName(hWnd);
        if (string.IsNullOrEmpty(procName)) return false;

        foreach (var excluded in excludedProcesses)
        {
            if (procName.Equals(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static Rect GetExtendedFrameBounds(nint hWnd)
    {
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT dwmRect, Marshal.SizeOf<RECT>()) == 0)
        {
            return new Rect(dwmRect.Left, dwmRect.Top, Math.Max(0, dwmRect.Right - dwmRect.Left), Math.Max(0, dwmRect.Bottom - dwmRect.Top));
        }

        GetWindowRect(hWnd, out RECT winRect);
        return new Rect(winRect.Left, winRect.Top, Math.Max(0, winRect.Right - winRect.Left), Math.Max(0, winRect.Bottom - winRect.Top));
    }
}
