using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using ZenithApp.Core.Logging;
using ZenithApp.Modules.RadialSwitcher;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.NextSession;

public sealed class RunningAppItem : ViewModelBase
{
    private bool _isTerminating;

    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string ExePath { get; init; } = string.Empty;
    public IReadOnlyList<nint> WindowHandles { get; init; } = Array.Empty<nint>();
    public IReadOnlyList<int> ProcessIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> UwpChildProcessIds { get; init; } = Array.Empty<int>();
    public bool IsExplorer { get; init; }
    public bool IsUwpApp { get; init; }
    public ImageSource? Icon { get; init; }

    public bool HasIcon => Icon != null;

    public bool IsTerminating
    {
        get => _isTerminating;
        set
        {
            if (SetProperty(ref _isTerminating, value))
            {
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    public bool IsIdle => !IsTerminating;

    public string ToolTipText => string.IsNullOrWhiteSpace(DisplayName)
        ? "Завершить программу"
        : $"Завершить «{DisplayName}»";
}

public readonly record struct TerminationResult(bool Success, bool AccessDenied, string? ErrorMessage = null);

public interface IRunningAppsTerminationService
{
    IReadOnlyList<RunningAppItem> GetAltTabRunningApps();
    Task<TerminationResult> TerminateAppAsync(RunningAppItem app, CancellationToken cancellationToken = default);
}

public sealed class RunningAppsTerminationService : IRunningAppsTerminationService
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint GW_OWNER = 4;
    private const int DWMWA_CLOAKED = 14;
    private const uint WM_CLOSE = 0x0010;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ERROR_ACCESS_DENIED = 5;

    private static readonly HashSet<string> IgnoredShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "TextInputHost",
        "SearchHost",
        "SearchApp",
        "StartMenuExperienceHost",
        "ShellExperienceHost",
        "LockApp",
        "ScreenClippingHost",
        "SystemSettingsBroker"
    };

    private static readonly HashSet<string> IgnoredWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
        "MultitaskingViewFrame",
        "ForegroundStaging",
        "TaskListThumbnailWnd"
    };

    private readonly IAppLogger _logger;

    public RunningAppsTerminationService(IAppLogger logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<RunningAppItem> GetAltTabRunningApps()
    {
        var entries = new List<RawWindowEntry>();

        EnumWindows((hWnd, _) =>
        {
            var entry = TryReadAltTabWindow(hWnd);
            if (entry != null)
            {
                entries.Add(entry);
            }
            return true;
        }, nint.Zero);

        var grouped = new Dictionary<string, MutableGroup>(StringComparer.OrdinalIgnoreCase);
        var orderedKeys = new List<string>();

        foreach (var entry in entries)
        {
            string groupKey;
            if (entry.IsUwpApp)
            {
                groupKey = $"UWP:{entry.Hwnd}";
            }
            else if (entry.IsExplorer)
            {
                groupKey = "EXPLORER";
            }
            else if (!string.IsNullOrWhiteSpace(entry.ExePath))
            {
                groupKey = $"EXE:{entry.ExePath.Trim().ToUpperInvariant()}";
            }
            else if (!string.IsNullOrWhiteSpace(entry.ProcessName))
            {
                groupKey = $"PROC:{entry.ProcessName.Trim().ToUpperInvariant()}";
            }
            else
            {
                groupKey = $"PID:{entry.ProcessId}";
            }

            if (!grouped.TryGetValue(groupKey, out var group))
            {
                group = new MutableGroup
                {
                    Key = groupKey,
                    DisplayName = entry.DisplayName,
                    ProcessName = entry.ProcessName,
                    ExePath = entry.ExePath,
                    IsExplorer = entry.IsExplorer,
                    IsUwpApp = entry.IsUwpApp,
                    Icon = entry.Icon
                };
                grouped[groupKey] = group;
                orderedKeys.Add(groupKey);
            }

            group.WindowHandles.Add(entry.Hwnd);
            group.ProcessIds.Add(entry.ProcessId);
            if (entry.UwpChildProcessId > 0)
            {
                group.UwpChildProcessIds.Add(entry.UwpChildProcessId);
            }

            if (group.Icon == null && entry.Icon != null)
            {
                group.Icon = entry.Icon;
            }
        }

        var result = new List<RunningAppItem>(orderedKeys.Count);
        foreach (var key in orderedKeys)
        {
            var g = grouped[key];
            result.Add(new RunningAppItem
            {
                Key = g.Key,
                DisplayName = g.DisplayName,
                ProcessName = g.ProcessName,
                ExePath = g.ExePath,
                WindowHandles = g.WindowHandles,
                ProcessIds = g.ProcessIds.ToList(),
                UwpChildProcessIds = g.UwpChildProcessIds.ToList(),
                IsExplorer = g.IsExplorer,
                IsUwpApp = g.IsUwpApp,
                Icon = g.Icon
            });
        }

        return result;
    }

    public async Task<TerminationResult> TerminateAppAsync(RunningAppItem app, CancellationToken cancellationToken = default)
    {
        if (app == null)
        {
            return new TerminationResult(false, false);
        }

        return await Task.Run(() => TerminateAppCore(app, cancellationToken), cancellationToken);
    }

    private TerminationResult TerminateAppCore(RunningAppItem app, CancellationToken cancellationToken)
    {
        bool anyAccessDenied = false;

        // 1. Отправляем WM_CLOSE во все окна Alt+Tab этой программы
        foreach (var hwnd in app.WindowHandles)
        {
            if (hwnd == nint.Zero || !IsWindow(hwnd)) continue;

            if (!PostMessage(hwnd, WM_CLOSE, nint.Zero, nint.Zero))
            {
                if (Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED)
                {
                    anyAccessDenied = true;
                }
            }
        }

        // 2. Проводник Windows (explorer.exe) никогда не убиваем — закрываем только его окна папок
        if (app.IsExplorer)
        {
            var closed = WaitUntilWindowsClosed(app.WindowHandles, checkCloaked: false, timeoutMs: 1500, cancellationToken);
            return new TerminationResult(
                closed,
                !closed && anyAccessDenied,
                !closed && anyAccessDenied ? $"Не удалось закрыть «{app.DisplayName}»: требуются права администратора." : null);
        }

        // 3. UWP-приложения (ApplicationFrameHost.exe): ждём закрытия/сворачивания фрейма, при зависании завершаем только дочерний UWP-процесс
        if (app.IsUwpApp)
        {
            if (WaitUntilWindowsClosed(app.WindowHandles, checkCloaked: true, timeoutMs: 1500, cancellationToken))
            {
                return new TerminationResult(true, false);
            }

            foreach (var childPid in app.UwpChildProcessIds)
            {
                if (childPid <= 0 || childPid == Environment.ProcessId) continue;
                var killed = TryKillProcess(childPid, entireTree: true, out var denied);
                if (denied) anyAccessDenied = true;
                if (killed)
                {
                    _logger.LogInfo($"[NextSession] Terminated UWP child process {childPid} for '{app.DisplayName}'.");
                }
            }

            var gone = WaitUntilWindowsClosed(app.WindowHandles, checkCloaked: true, timeoutMs: 800, cancellationToken);
            return new TerminationResult(
                gone,
                !gone && anyAccessDenied,
                !gone && anyAccessDenied ? $"Не удалось завершить «{app.DisplayName}»: требуются права администратора." : null);
        }

        // 4. Обычные приложения: ждём корректного выхода после WM_CLOSE.
        // Если приложение спрятало окно в трей (Telegram/Discord/Steam), процесс остаётся жить — завершаем его принудительно.
        var deadline = Environment.TickCount64 + 1200;
        long? hiddenAt = null;

        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (AreAllProcessesExited(app.ProcessIds))
            {
                return new TerminationResult(true, false);
            }

            bool allWindowsGone = AreAllWindowsClosed(app.WindowHandles, checkCloaked: false);
            if (allWindowsGone)
            {
                hiddenAt ??= Environment.TickCount64;
                if (Environment.TickCount64 - hiddenAt.Value >= 350)
                {
                    break;
                }
            }

            Thread.Sleep(80);
        }

        if (AreAllProcessesExited(app.ProcessIds))
        {
            return new TerminationResult(true, false);
        }

        foreach (var pid in app.ProcessIds)
        {
            if (pid <= 0 || pid == Environment.ProcessId) continue;

            if (!TryKillProcess(pid, entireTree: true, out var denied) && denied)
            {
                anyAccessDenied = true;
            }
        }

        var exitedAfterKill = WaitUntilProcessesExited(app.ProcessIds, timeoutMs: 800, cancellationToken);
        var windowsGoneAfterKill = AreAllWindowsClosed(app.WindowHandles, checkCloaked: false);
        var success = exitedAfterKill || windowsGoneAfterKill;

        if (!success && anyAccessDenied)
        {
            return new TerminationResult(
                false,
                true,
                $"Не удалось завершить «{app.DisplayName}»: требуются права администратора.");
        }

        return new TerminationResult(success, false);
    }

    private static RawWindowEntry? TryReadAltTabWindow(nint hWnd)
    {
        if (hWnd == nint.Zero || !IsWindowVisible(hWnd)) return null;
        if (IsWindowCloaked(hWnd)) return null;

        var exStyle = (long)WindowListService.GetWindowLongPtr(hWnd, GWL_EXSTYLE);
        bool isAppWindow = (exStyle & WS_EX_APPWINDOW) != 0;
        bool isToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;
        bool isNoActivate = (exStyle & WS_EX_NOACTIVATE) != 0;

        if (!isAppWindow && (isToolWindow || isNoActivate))
        {
            return null;
        }

        if (!isAppWindow)
        {
            var owner = GetWindow(hWnd, GW_OWNER);
            if (owner != nint.Zero && IsWindowVisible(owner))
            {
                return null;
            }
        }

        if (!WindowListService.IsIconic(hWnd))
        {
            if (!GetWindowRect(hWnd, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            {
                return null;
            }
        }

        var titleLength = GetWindowTextLength(hWnd);
        if (titleLength <= 0) return null;

        var titleBuilder = new StringBuilder(titleLength + 1);
        GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
        var title = titleBuilder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(title)) return null;

        WindowListService.GetWindowThreadProcessId(hWnd, out var pid);
        if (pid == 0 || pid == Environment.ProcessId) return null;

        var className = GetWindowClassName(hWnd);
        if (IgnoredWindowClasses.Contains(className)) return null;

        var (procName, exePath) = GetProcessDetails(pid);
        if (string.IsNullOrWhiteSpace(procName)) return null;
        if (IgnoredShellProcesses.Contains(procName)) return null;

        bool isExplorer = procName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
        if (isExplorer)
        {
            if (!className.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase) &&
                !className.Equals("ExploreWClass", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        bool isUwpApp = procName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase);
        int uwpChildPid = 0;
        string displayName = isExplorer ? "Проводник" : (isUwpApp ? title : procName);
        string iconLookupExePath = exePath;

        if (isUwpApp)
        {
            uwpChildPid = FindUwpChildProcessId(hWnd, (int)pid);
            if (uwpChildPid > 0)
            {
                var (_, childExePath) = GetProcessDetails((uint)uwpChildPid);
                if (!string.IsNullOrWhiteSpace(childExePath))
                {
                    iconLookupExePath = childExePath;
                }
            }
        }

        var icon = WindowListService.GetWindowOrProcessIcon(hWnd, iconLookupExePath);
        if (icon == null && !string.IsNullOrWhiteSpace(iconLookupExePath))
        {
            icon = WindowListService.GetIconForPath(iconLookupExePath);
        }

        return new RawWindowEntry
        {
            Hwnd = hWnd,
            ProcessId = (int)pid,
            UwpChildProcessId = uwpChildPid,
            DisplayName = displayName,
            ProcessName = procName,
            ExePath = exePath,
            IsExplorer = isExplorer,
            IsUwpApp = isUwpApp,
            Icon = icon
        };
    }

    private static int FindUwpChildProcessId(nint parentHwnd, int framePid)
    {
        int foundPid = 0;

        EnumChildWindows(parentHwnd, (childHwnd, _) =>
        {
            WindowListService.GetWindowThreadProcessId(childHwnd, out var childPid);
            if (childPid != 0 && childPid != framePid)
            {
                foundPid = (int)childPid;
                return false;
            }
            return true;
        }, nint.Zero);

        return foundPid;
    }

    private static bool TryKillProcess(int pid, bool entireTree, out bool accessDenied)
    {
        accessDenied = false;

        try
        {
            using var proc = Process.GetProcessById(pid);
            if (proc.HasExited) return true;

            try
            {
                proc.Kill(entireProcessTree: entireTree);
            }
            catch (Win32Exception)
            {
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch
            {
                if (!proc.HasExited)
                {
                    proc.Kill();
                }
            }

            proc.WaitForExit(1000);
            return true;
        }
        catch (ArgumentException)
        {
            // Процесс уже завершился
            return true;
        }
        catch (InvalidOperationException)
        {
            // Процесс уже завершился
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_ACCESS_DENIED)
        {
            accessDenied = true;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            accessDenied = true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool WaitUntilWindowsClosed(
        IReadOnlyList<nint> handles,
        bool checkCloaked,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AreAllWindowsClosed(handles, checkCloaked))
            {
                return true;
            }
            Thread.Sleep(80);
        }

        return AreAllWindowsClosed(handles, checkCloaked);
    }

    private static bool AreAllWindowsClosed(IReadOnlyList<nint> handles, bool checkCloaked)
    {
        foreach (var hwnd in handles)
        {
            if (hwnd == nint.Zero) continue;
            if (!IsWindow(hwnd)) continue;
            if (!IsWindowVisible(hwnd)) continue;
            if (checkCloaked && IsWindowCloaked(hwnd)) continue;
            return false;
        }

        return true;
    }

    private static bool WaitUntilProcessesExited(IReadOnlyList<int> pids, int timeoutMs, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AreAllProcessesExited(pids))
            {
                return true;
            }
            Thread.Sleep(80);
        }

        return AreAllProcessesExited(pids);
    }

    private static bool AreAllProcessesExited(IReadOnlyList<int> pids)
    {
        foreach (var pid in pids)
        {
            if (pid <= 0) continue;
            try
            {
                using var proc = Process.GetProcessById(pid);
                if (!proc.HasExited)
                {
                    return false;
                }
            }
            catch (ArgumentException)
            {
                // Уже завершился
            }
            catch (InvalidOperationException)
            {
                // Уже завершился
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWindowCloaked(nint hWnd)
    {
        return DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    private static string GetWindowClassName(nint hWnd)
    {
        var sb = new StringBuilder(256);
        int len = GetClassName(hWnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString() : string.Empty;
    }

    private static (string procName, string exePath) GetProcessDetails(uint pid)
    {
        nint hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess != nint.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                {
                    string path = sb.ToString();
                    string name = Path.GetFileNameWithoutExtension(path);
                    return (name, path);
                }
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return (proc.ProcessName, proc.MainModule?.FileName ?? string.Empty);
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }

    private sealed class RawWindowEntry
    {
        public nint Hwnd { get; init; }
        public int ProcessId { get; init; }
        public int UwpChildProcessId { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public string ProcessName { get; init; } = string.Empty;
        public string ExePath { get; init; } = string.Empty;
        public bool IsExplorer { get; init; }
        public bool IsUwpApp { get; init; }
        public ImageSource? Icon { get; init; }
    }

    private sealed class MutableGroup
    {
        public string Key { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string ProcessName { get; init; } = string.Empty;
        public string ExePath { get; init; } = string.Empty;
        public bool IsExplorer { get; init; }
        public bool IsUwpApp { get; init; }
        public ImageSource? Icon { get; set; }
        public List<nint> WindowHandles { get; } = new();
        public HashSet<int> ProcessIds { get; } = new();
        public HashSet<int> UwpChildProcessIds { get; } = new();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint hWndParent, EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hWnd, uint uCmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(nint hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);
}
