using System.Runtime.InteropServices;
using System.Windows;

namespace ZenithApp.Modules.QuickLook;

public static class WindowPlacementHelper
{
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint hWnd);

    public struct MonitorWorkArea
    {
        public double Left;
        public double Top;
        public double Width;
        public double Height;
    }

    public static MonitorWorkArea GetCurrentTargetMonitorWorkArea()
    {
        var fgHwnd = GetForegroundWindow();
        var hMonitor = MonitorFromWindow(fgHwnd, MONITOR_DEFAULTTONEAREST);

        var mi = new MONITORINFO();
        mi.cbSize = Marshal.SizeOf<MONITORINFO>();

        if (GetMonitorInfo(hMonitor, ref mi))
        {
            return new MonitorWorkArea
            {
                Left = mi.rcWork.Left,
                Top = mi.rcWork.Top,
                Width = mi.rcWork.Right - mi.rcWork.Left,
                Height = mi.rcWork.Bottom - mi.rcWork.Top
            };
        }

        // Fallback to primary screen
        return new MonitorWorkArea
        {
            Left = SystemParameters.WorkArea.Left,
            Top = SystemParameters.WorkArea.Top,
            Width = SystemParameters.WorkArea.Width,
            Height = SystemParameters.WorkArea.Height
        };
    }
}
