using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ZenithApp.Modules.QuickLook;

public static class WindowPlacementHelper
{
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;

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

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public struct MonitorWorkArea
    {
        public double Left;
        public double Top;
        public double Width;
        public double Height;
        public int PhysicalLeft;
        public int PhysicalTop;
        public int PhysicalWidth;
        public int PhysicalHeight;
        public double ScaleX;
        public double ScaleY;
    }

    public static MonitorWorkArea GetCurrentTargetMonitorWorkArea(nint preferredHwnd = 0)
    {
        var targetHwnd = preferredHwnd != nint.Zero ? preferredHwnd : GetForegroundWindow();
        var hMonitor = MonitorFromWindow(targetHwnd, MONITOR_DEFAULTTONEAREST);

        var mi = new MONITORINFO();
        mi.cbSize = Marshal.SizeOf<MONITORINFO>();

        if (GetMonitorInfo(hMonitor, ref mi))
        {
            double scaleX = 1.0;
            double scaleY = 1.0;
            try
            {
                if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0 && dpiX > 0 && dpiY > 0)
                {
                    scaleX = dpiX / 96.0;
                    scaleY = dpiY / 96.0;
                }
            }
            catch
            {
                // Fallback to 1.0 if shcore.dll is unavailable
            }

            int physW = mi.rcWork.Right - mi.rcWork.Left;
            int physH = mi.rcWork.Bottom - mi.rcWork.Top;

            return new MonitorWorkArea
            {
                Left = mi.rcWork.Left / scaleX,
                Top = mi.rcWork.Top / scaleY,
                Width = physW / scaleX,
                Height = physH / scaleY,
                PhysicalLeft = mi.rcWork.Left,
                PhysicalTop = mi.rcWork.Top,
                PhysicalWidth = physW,
                PhysicalHeight = physH,
                ScaleX = scaleX,
                ScaleY = scaleY
            };
        }

        // Fallback to primary screen (already in DIPs)
        var wa = SystemParameters.WorkArea;
        return new MonitorWorkArea
        {
            Left = wa.Left,
            Top = wa.Top,
            Width = wa.Width,
            Height = wa.Height,
            PhysicalLeft = (int)Math.Round(wa.Left),
            PhysicalTop = (int)Math.Round(wa.Top),
            PhysicalWidth = (int)Math.Round(wa.Width),
            PhysicalHeight = (int)Math.Round(wa.Height),
            ScaleX = 1.0,
            ScaleY = 1.0
        };
    }

    public static void PositionWindowCentered(Window window, MonitorWorkArea area, double dipWidth, double dipHeight)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != nint.Zero && area.PhysicalWidth > 0 && area.PhysicalHeight > 0)
        {
            // Move onto the target monitor first so PerMonitorV2 DPI settles before sizing
            SetWindowPos(hwnd, nint.Zero, area.PhysicalLeft, area.PhysicalTop, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

            double scaleX = area.ScaleX > 0 ? area.ScaleX : 1.0;
            double scaleY = area.ScaleY > 0 ? area.ScaleY : 1.0;

            int physW = Math.Max(1, (int)Math.Round(dipWidth * scaleX));
            int physH = Math.Max(1, (int)Math.Round(dipHeight * scaleY));
            int physLeft = area.PhysicalLeft + (area.PhysicalWidth - physW) / 2;
            int physTop = area.PhysicalTop + (area.PhysicalHeight - physH) / 2;

            window.Width = dipWidth;
            window.Height = dipHeight;
            SetWindowPos(hwnd, nint.Zero, physLeft, physTop, physW, physH, SWP_NOZORDER | SWP_NOACTIVATE);
            return;
        }

        window.Width = dipWidth;
        window.Height = dipHeight;
        window.Left = area.Left + (area.Width - dipWidth) / 2.0;
        window.Top = area.Top + (area.Height - dipHeight) / 2.0;
    }
}
