using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ZenithApp.Modules.QuickLook;

public static class ExplorerSelectionService
{
    private const int CchMax = 256;

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern nint GetFocus();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern nint AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint FindWindowEx(nint parentHandle, nint childAfter, string className, string? windowTitle);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
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

    public static string GetWindowClassName(nint hWnd)
    {
        if (hWnd == nint.Zero) return string.Empty;
        var sb = new StringBuilder(CchMax);
        GetClassName(hWnd, sb, CchMax);
        return sb.ToString();
    }

    public static bool AreModifiersPressed()
    {
        const int VK_SHIFT = 0x10;
        const int VK_CONTROL = 0x11;
        const int VK_MENU = 0x12; // Alt
        const int VK_LWIN = 0x5B;
        const int VK_RWIN = 0x5C;

        return (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 ||
               (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 ||
               (GetAsyncKeyState(VK_MENU) & 0x8000) != 0 ||
               (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 ||
               (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
    }

    /// <summary>
    /// Fast check performed directly in the keyboard hook to decide if space might be intended for Explorer file preview.
    /// Returns true if foreground window is Explorer or Desktop and the focused control is NOT an edit / input box.
    /// </summary>
    public static bool IsExplorerOrDesktopActiveFast()
    {
        var foregroundHwnd = GetForegroundWindow();
        if (foregroundHwnd == nint.Zero) return false;

        var fgClass = GetWindowClassName(foregroundHwnd);
        bool isExplorer = fgClass.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase);
        bool isDesktop = fgClass.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                         fgClass.Equals("WorkerW", StringComparison.OrdinalIgnoreCase);

        if (!isExplorer && !isDesktop)
        {
            return false;
        }

        // Check if focused element in that foreground window is a text input
        var focusHwnd = GetFocusWindowOfThread(foregroundHwnd);
        if (focusHwnd != nint.Zero)
        {
            var focusClass = GetWindowClassName(focusHwnd);

            // If focus is in an edit/search/address box, space MUST go to the textbox (typing)
            if (focusClass.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
                focusClass.IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                focusClass.IndexOf("Search", StringComparison.OrdinalIgnoreCase) >= 0 ||
                focusClass.IndexOf("ComboBox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                focusClass.IndexOf("AddressBandRoot", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static nint GetFocusWindowOfThread(nint hWnd)
    {
        uint threadId = GetWindowThreadProcessId(hWnd, out _);
        if (threadId == 0) return nint.Zero;

        var gui = new GUITHREADINFO();
        gui.cbSize = Marshal.SizeOf<GUITHREADINFO>();
        if (GetGUIThreadInfo(threadId, ref gui) && gui.hwndFocus != nint.Zero)
        {
            return gui.hwndFocus;
        }

        return nint.Zero;
    }

    /// <summary>
    /// Resolves the single selected file from Explorer or Desktop using Shell COM interfaces.
    /// Returns null if multiple items, 0 items, a folder, or error.
    /// </summary>
    public static string? GetSelectedFilePath()
    {
        var foregroundHwnd = GetForegroundWindow();
        if (foregroundHwnd == nint.Zero) return null;

        var fgClass = GetWindowClassName(foregroundHwnd);
        bool isExplorer = fgClass.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase);
        bool isDesktop = fgClass.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                         fgClass.Equals("WorkerW", StringComparison.OrdinalIgnoreCase);

        if (isExplorer)
        {
            return GetSelectedFileFromExplorerWindow(foregroundHwnd);
        }
        else if (isDesktop)
        {
            return GetSelectedFileFromDesktop();
        }

        return null;
    }

    private static string? GetSelectedFileFromExplorerWindow(nint foregroundHwnd)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic windows = shell.Windows();
            int count = windows.Count;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    if (window == null) continue;

#pragma warning disable CS8602
                    nint hwnd = (nint)window.HWND;
                    if (hwnd == foregroundHwnd)
                    {
                        dynamic document = window.Document;
                        if (document == null) continue;

                        dynamic? selectedItems = document.SelectedItems();
                        if (selectedItems == null || (int)selectedItems.Count != 1)
                        {
                            return null; // Not exactly 1 item selected
                        }

                        dynamic? item = selectedItems.Item(0);
                        if (item == null || (bool)item.IsFolder)
                        {
                            return null; // Ignore folder
                        }

                        string? path = (string?)item.Path;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            return path;
                        }
                    }
#pragma warning restore CS8602
                }
                catch
                {
                    // Continue to next window
                }
            }
        }
        catch
        {
            // COM failure
        }

        return null;
    }

    private static string? GetSelectedFileFromDesktop()
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic windows = shell.Windows();
            int count = windows.Count;

            // In Shell.Application, desktop view can be obtained by matching explorer windows where Document.Folder exists or desktop hwnd
            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    if (window == null) continue;

                    dynamic doc = window.Document;
                    if (doc == null) continue;

#pragma warning disable CS8602
                    // Check if document view is desktop
                    dynamic? selectedItems = doc.SelectedItems();
                    if (selectedItems != null && (int)selectedItems.Count == 1)
                    {
                        dynamic? item = selectedItems.Item(0);
                        if (item != null && !(bool)item.IsFolder)
                        {
                            string? path = (string?)item.Path;
                            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            {
                                return path;
                            }
                        }
                    }
#pragma warning restore CS8602
                }
                catch
                {
                    // Ignore
                }
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }
}
