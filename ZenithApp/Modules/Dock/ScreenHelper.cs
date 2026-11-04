using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace ZenithApp.Modules.Dock;

public struct ScreenBounds
{
    public double Left;
    public double Top;
    public double Width;
    public double Height;
    public double WorkingLeft;
    public double WorkingTop;
    public double WorkingWidth;
    public double WorkingHeight;
}

public static class ScreenHelper
{
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    public static ScreenBounds GetTargetScreenBounds(DockMonitorTarget target, Visual visual)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        nint hMonitor = nint.Zero;

        if (target == DockMonitorTarget.Cursor)
        {
            if (GetCursorPos(out var pt))
            {
                hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            }
        }

        if (hMonitor == nint.Zero)
        {
            hMonitor = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
        }

        if (hMonitor != nint.Zero && GetMonitorInfo(hMonitor, ref mi))
        {
            var dpi = VisualTreeHelper.GetDpi(visual);
            var dpiX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            var dpiY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

            return new ScreenBounds
            {
                Left = mi.rcMonitor.Left / dpiX,
                Top = mi.rcMonitor.Top / dpiY,
                Width = (mi.rcMonitor.Right - mi.rcMonitor.Left) / dpiX,
                Height = (mi.rcMonitor.Bottom - mi.rcMonitor.Top) / dpiY,
                WorkingLeft = mi.rcWork.Left / dpiX,
                WorkingTop = mi.rcWork.Top / dpiY,
                WorkingWidth = (mi.rcWork.Right - mi.rcWork.Left) / dpiX,
                WorkingHeight = (mi.rcWork.Bottom - mi.rcWork.Top) / dpiY
            };
        }

        // Fallback to primary WPF SystemParameters
        return new ScreenBounds
        {
            Left = 0,
            Top = 0,
            Width = SystemParameters.PrimaryScreenWidth,
            Height = SystemParameters.PrimaryScreenHeight,
            WorkingLeft = SystemParameters.WorkArea.Left,
            WorkingTop = SystemParameters.WorkArea.Top,
            WorkingWidth = SystemParameters.WorkArea.Width,
            WorkingHeight = SystemParameters.WorkArea.Height
        };
    }
}
