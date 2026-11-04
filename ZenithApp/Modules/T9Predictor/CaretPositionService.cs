using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace ZenithApp.Modules.T9Predictor;

public sealed class CaretPositionInfo
{
    public Point CaretScreenLocation { get; }
    public double CaretHeight { get; }
    public bool IsDarkBackground { get; }

    public CaretPositionInfo(Point caretScreenLocation, double caretHeight, bool isDarkBackground)
    {
        CaretScreenLocation = caretScreenLocation;
        CaretHeight = caretHeight;
        IsDarkBackground = isDarkBackground;
    }
}

public static class CaretPositionService
{
    private const int ES_PASSWORD = 0x0020;
    private const int GWL_STYLE = -16;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(nint hdc, int nXPos, int nYPos);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    public static bool IsPasswordControl(nint hwndFocus)
    {
        if (hwndFocus == nint.Zero) return false;

        // Check Win32 Edit ES_PASSWORD
        long style = nint.Size == 8 ? (long)GetWindowLongPtr64(hwndFocus, GWL_STYLE) : GetWindowLong32(hwndFocus, GWL_STYLE);
        if ((style & ES_PASSWORD) != 0)
        {
            return true;
        }

        return false;
    }

    public static CaretPositionInfo? TryGetCaretPosition(nint foregroundHwnd)
    {
        if (foregroundHwnd == nint.Zero) return null;

        uint threadId = GetWindowThreadProcessId(foregroundHwnd, out _);
        if (threadId == 0) return null;

        var gui = new GUITHREADINFO();
        gui.cbSize = Marshal.SizeOf<GUITHREADINFO>();

        if (GetGUIThreadInfo(threadId, ref gui))
        {
            // If focus is a password field, immediately reject
            if (IsPasswordControl(gui.hwndFocus))
            {
                return null;
            }

            // 1. Try Win32 Caret in focused window
            nint targetHwnd = gui.hwndCaret != nint.Zero ? gui.hwndCaret : gui.hwndFocus;
            if (targetHwnd != nint.Zero && (gui.rcCaret.Right > gui.rcCaret.Left || gui.rcCaret.Bottom > gui.rcCaret.Top))
            {
                var pt = new POINT { X = gui.rcCaret.Right, Y = gui.rcCaret.Top };
                if (ClientToScreen(targetHwnd, ref pt))
                {
                    double height = Math.Max(14, gui.rcCaret.Bottom - gui.rcCaret.Top);
                    bool isDark = IsPixelDarkAt(pt.X + 4, pt.Y + (int)(height / 2));
                    return new CaretPositionInfo(new Point(pt.X, pt.Y), height, isDark);
                }
            }
        }

        return null;
    }

    private static bool IsPixelDarkAt(int screenX, int screenY)
    {
        try
        {
            nint hdc = GetDC(nint.Zero); // Screen DC
            if (hdc != nint.Zero)
            {
                uint pixel = GetPixel(hdc, screenX, screenY);
                ReleaseDC(nint.Zero, hdc);

                if (pixel != 0xFFFFFFFF)
                {
                    int r = (int)(pixel & 0xFF);
                    int g = (int)((pixel >> 8) & 0xFF);
                    int b = (int)((pixel >> 16) & 0xFF);

                    // Perceived brightness
                    double luminance = (0.299 * r + 0.587 * g + 0.114 * b);
                    return luminance < 128; // Below 128 is dark background
                }
            }
        }
        catch { }

        return false; // Default assume light background
    }
}
