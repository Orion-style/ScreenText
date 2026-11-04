using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace ZenithApp.Modules.SnapZones;

public static class WindowPlacementService
{
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const int SW_RESTORE = 9;

    private const int GWL_STYLE = -16;
    private const long WS_THICKFRAME = 0x00040000L;
    private const long WS_MAXIMIZEBOX = 0x00010000L;

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
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

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

    public static long GetWindowStyle(nint hWnd)
    {
        if (nint.Size == 8)
            return (long)GetWindowLongPtr64(hWnd, GWL_STYLE);
        return GetWindowLong32(hWnd, GWL_STYLE);
    }

    public static bool IsResizableWindow(nint hWnd)
    {
        long style = GetWindowStyle(hWnd);
        return (style & WS_THICKFRAME) != 0;
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

    public static bool IsSnapCandidate(nint hWnd, IEnumerable<string> excludedProcesses)
    {
        if (hWnd == nint.Zero) return false;

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

    public static bool SnapWindowToZone(nint hWnd, Rect targetBounds)
    {
        if (hWnd == nint.Zero) return false;

        try
        {
            // If window is maximized, restore it first
            if (IsZoomed(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }

            GetWindowRect(hWnd, out RECT normalRect);
            Rect frameRect = GetExtendedFrameBounds(hWnd);

            // Compute shadow/margin difference
            int borderLeft = (int)(frameRect.Left - normalRect.Left);
            int borderTop = (int)(frameRect.Top - normalRect.Top);
            int borderRight = (int)(normalRect.Right - frameRect.Right);
            int borderBottom = (int)(normalRect.Bottom - frameRect.Bottom);

            // Adjust target bounds so that the visual frame aligns exactly with zone rect
            int finalX = (int)targetBounds.Left - borderLeft;
            int finalY = (int)targetBounds.Top - borderTop;
            int finalW = (int)targetBounds.Width + borderLeft + borderRight;
            int finalH = (int)targetBounds.Height + borderTop + borderBottom;

            return SetWindowPos(hWnd, nint.Zero, finalX, finalY, finalW, finalH, SWP_NOZORDER | SWP_FRAMECHANGED);
        }
        catch
        {
            return false;
        }
    }
}
