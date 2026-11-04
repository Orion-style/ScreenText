using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace ZenithApp.Modules.SnapZones;

public sealed class MonitorInfoData
{
    public nint Handle { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public Rect MonitorBounds { get; set; }
    public Rect WorkArea { get; set; }
    public double DpiScaleX { get; set; } = 1.0;
    public double DpiScaleY { get; set; } = 1.0;
    public bool IsPrimary { get; set; }
}

public static class MultiMonitorHelper
{
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const int CCHDEVICENAME = 32;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public Rect ToRect() => new Rect(Left, Top, Math.Max(0, Right - Left), Math.Max(0, Bottom - Top));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    public static List<MonitorInfoData> GetAllMonitors()
    {
        var monitors = new List<MonitorInfoData>();

        EnumDisplayMonitors(nint.Zero, nint.Zero, (nint hMon, nint hdc, ref RECT rc, nint data) =>
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();
            if (GetMonitorInfo(hMon, ref mi))
            {
                double scaleX = 1.0;
                double scaleY = 1.0;
                try
                {
                    // MDT_EFFECTIVE_DPI = 0
                    if (GetDpiForMonitor(hMon, 0, out uint dpiX, out uint dpiY) == 0)
                    {
                        scaleX = dpiX / 96.0;
                        scaleY = dpiY / 96.0;
                    }
                }
                catch
                {
                    // Fallback to 1.0
                }

                monitors.Add(new MonitorInfoData
                {
                    Handle = hMon,
                    DeviceName = mi.szDevice ?? "Display",
                    MonitorBounds = mi.rcMonitor.ToRect(),
                    WorkArea = mi.rcWork.ToRect(),
                    DpiScaleX = scaleX,
                    DpiScaleY = scaleY,
                    IsPrimary = (mi.dwFlags & 1) != 0
                });
            }
            return true;
        }, nint.Zero);

        return monitors;
    }

    public static MonitorInfoData? GetMonitorFromCursor()
    {
        if (!GetCursorPos(out POINT pt)) return null;
        var hMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        return GetMonitorByHandle(hMon);
    }

    public static MonitorInfoData? GetMonitorFromWindow(nint hWnd)
    {
        if (hWnd == nint.Zero) return null;
        var hMon = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
        return GetMonitorByHandle(hMon);
    }

    public static MonitorInfoData? GetMonitorByHandle(nint hMon)
    {
        if (hMon == nint.Zero) return null;
        var mi = new MONITORINFOEX();
        mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();
        if (GetMonitorInfo(hMon, ref mi))
        {
            double scaleX = 1.0;
            double scaleY = 1.0;
            try
            {
                if (GetDpiForMonitor(hMon, 0, out uint dpiX, out uint dpiY) == 0)
                {
                    scaleX = dpiX / 96.0;
                    scaleY = dpiY / 96.0;
                }
            }
            catch { }

            return new MonitorInfoData
            {
                Handle = hMon,
                DeviceName = mi.szDevice ?? "Display",
                MonitorBounds = mi.rcMonitor.ToRect(),
                WorkArea = mi.rcWork.ToRect(),
                DpiScaleX = scaleX,
                DpiScaleY = scaleY,
                IsPrimary = (mi.dwFlags & 1) != 0
            };
        }
        return null;
    }
}
