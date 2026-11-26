using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ZenithApp.Modules.StickyNotes;

internal static class StickyNoteWindowInterop
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    public static void ApplyExtendedStyle(Window window, bool clickThrough, bool noActivate)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == nint.Zero) return;

        long exStyle = nint.Size == 8
            ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE)
            : GetWindowLong32(hwnd, GWL_EXSTYLE);

        exStyle |= WS_EX_TOOLWINDOW;

        if (clickThrough)
            exStyle |= WS_EX_TRANSPARENT;
        else
            exStyle &= ~WS_EX_TRANSPARENT;

        if (noActivate)
            exStyle |= WS_EX_NOACTIVATE;
        else
            exStyle &= ~WS_EX_NOACTIVATE;

        if (nint.Size == 8)
            SetWindowLongPtr64(hwnd, GWL_EXSTYLE, (nint)exStyle);
        else
            SetWindowLong32(hwnd, GWL_EXSTYLE, (int)exStyle);

        SetWindowPos(
            hwnd,
            nint.Zero,
            0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }
}
