using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ZenithApp.Modules.Dock;

public static class ProcessInteractionService
{
    private const uint WM_CLOSE = 0x0010;
    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(
        nint hWnd,
        uint msg,
        nint wParam,
        nint lParam,
        uint fuFlags,
        uint uTimeout,
        out nuint lpdwResult);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
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

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _targetExeNameCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if a running process matches the given target path or process name.
    /// Fast check without heavy allocations.
    /// </summary>
    public static bool IsRunning(string targetPath, out Process? foundProcess)
    {
        foundProcess = null;
        if (string.IsNullOrWhiteSpace(targetPath)) return false;

        if (!_targetExeNameCache.TryGetValue(targetPath, out var exeName))
        {
            var resolvedTarget = IconExtractionService.ResolveShortcutTarget(targetPath);
            exeName = Path.GetFileNameWithoutExtension(resolvedTarget);
            _targetExeNameCache[targetPath] = exeName;
        }

        if (string.IsNullOrWhiteSpace(exeName)) return false;

        try
        {
            var matching = Process.GetProcessesByName(exeName);
            if (matching.Length > 0)
            {
                // Prefer process with valid MainWindowHandle
                foundProcess = matching.FirstOrDefault(p => p.MainWindowHandle != nint.Zero) ?? matching[0];
                return true;
            }
        }
        catch
        {
            // Ignore access errors on elevated or restricted processes
        }

        return false;
    }

    /// <summary>
    /// Brings the process's main window to foreground, restoring if minimized.
    /// </summary>
    public static bool BringToFront(Process process)
    {
        try
        {
            process.Refresh();
            var handle = process.MainWindowHandle;
            if (handle != nint.Zero)
            {
                if (IsIconic(handle))
                {
                    ShowWindowAsync(handle, SW_RESTORE);
                }
                else
                {
                    ShowWindowAsync(handle, SW_SHOW);
                }
                SetForegroundWindow(handle);
                return true;
            }
        }
        catch
        {
            // Ignore
        }
        return false;
    }

    /// <summary>
    /// Gracefully closes application by sending WM_CLOSE to main window.
    /// </summary>
    public static void CloseGracefully(Process process)
    {
        try
        {
            var handle = process.MainWindowHandle;
            if (handle != nint.Zero)
            {
                // Send WM_CLOSE with 2 second timeout to avoid blocking if hung
                SendMessageTimeout(handle, WM_CLOSE, nint.Zero, nint.Zero, 0x0002 /* SMTO_ABORTIFHUNG */, 2000, out _);
            }
            else
            {
                process.CloseMainWindow();
            }
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>
    /// Forcefully terminates process.
    /// </summary>
    public static void KillForcefully(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>
    /// Launches or brings to front an item.
    /// </summary>
    public static bool LaunchOrActivate(DockItem item)
    {
        if (string.IsNullOrWhiteSpace(item.TargetPath)) return false;

        if (IsRunning(item.TargetPath, out var proc) && proc != null)
        {
            if (BringToFront(proc)) return true;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = item.TargetPath,
                Arguments = item.Arguments,
                UseShellExecute = true
            };

            var dir = Path.GetDirectoryName(item.TargetPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                psi.WorkingDirectory = dir;
            }

            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether the foreground active window is currently running in true full-screen mode
    /// (e.g. game, video player, presentation), so dock can stay hidden.
    /// </summary>
    public static bool IsForegroundWindowFullScreen()
    {
        var fgHwnd = GetForegroundWindow();
        if (fgHwnd == nint.Zero) return false;

        // Skip Desktop / Shell
        var sb = new StringBuilder(256);
        GetClassName(fgHwnd, sb, sb.Capacity);
        var className = sb.ToString();
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            return false;
        }

        var hMonitor = MonitorFromWindow(fgHwnd, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == nint.Zero) return false;

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(hMonitor, ref mi))
        {
            if (GetWindowRect(fgHwnd, out var winRect))
            {
                // Fullscreen window bounds match or cover the entire monitor rectangle
                var isFullScreen = winRect.Left <= mi.rcMonitor.Left &&
                                   winRect.Top <= mi.rcMonitor.Top &&
                                   winRect.Right >= mi.rcMonitor.Right &&
                                   winRect.Bottom >= mi.rcMonitor.Bottom;
                return isFullScreen;
            }
        }

        return false;
    }
}
