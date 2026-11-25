using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using ZenithApp.Modules.RadialSwitcher;

namespace ZenithApp.Modules.NextSession;

public interface INextSessionProgramCatalogService
{
    IReadOnlyList<NextSessionProgramItem> LoadAvailablePrograms(
        IEnumerable<NextSessionProgramEntry>? customPrograms = null,
        IEnumerable<NextSessionProgramEntry>? pendingPrograms = null);

    ImageSource? ResolveIcon(string? executablePath, string? processName = null);
}

public sealed class NextSessionProgramCatalogService : INextSessionProgramCatalogService
{
    private static readonly HashSet<string> IgnoredShortcutWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "uninstall", "удалить", "деинсталлировать", "deinstall", "help", "readme",
        "documentation", "справка", "website", "installer", "zenith"
    };

    public IReadOnlyList<NextSessionProgramItem> LoadAvailablePrograms(
        IEnumerable<NextSessionProgramEntry>? customPrograms = null,
        IEnumerable<NextSessionProgramEntry>? pendingPrograms = null)
    {
        var itemsByKey = new Dictionary<string, NextSessionProgramItem>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var runningProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runningExePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var openWindowEntries = new List<(string Name, string ProcessName, string ExePath, ImageSource? Icon)>();

        CollectRunningApplications(runningProcessNames, runningExePaths, openWindowEntries);

        // 1. Пользовательские и уже запланированные программы (всегда присутствуют в сетке)
        foreach (var entry in (customPrograms ?? Enumerable.Empty<NextSessionProgramEntry>())
                 .Concat(pendingPrograms ?? Enumerable.Empty<NextSessionProgramEntry>()))
        {
            AddEntryIfValid(entry, isCustom: true);
        }

        // 2. Программы из меню «Пуск»
        var startMenuPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
        };

        foreach (var dir in startMenuPaths)
        {
            if (!Directory.Exists(dir)) continue;

            try
            {
                foreach (var lnk in Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories))
                {
                    try
                    {
                        var name = Path.GetFileNameWithoutExtension(lnk).Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (IgnoredShortcutWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;

                        if (!seenNames.Add(name)) continue;

                        var icon = WindowListService.GetIconForPath(lnk);
                        var isRunning = IsMatchRunning(name, lnk, runningProcessNames, runningExePaths);

                        var entry = new NextSessionProgramEntry
                        {
                            Name = name,
                            ExecutablePath = lnk,
                            ProcessName = name
                        };

                        var key = entry.GetNormalizedKey();
                        if (!itemsByKey.ContainsKey(key))
                        {
                            itemsByKey[key] = new NextSessionProgramItem
                            {
                                Key = key,
                                Name = name,
                                ExecutablePath = lnk,
                                ProcessName = name,
                                Icon = icon,
                                IsRunning = isRunning
                            };
                        }
                    }
                    catch
                    {
                        // Ignore inaccessible shortcut
                    }
                }
            }
            catch
            {
                // Ignore inaccessible directory
            }
        }

        // 3. Открытые в данный момент программы, если их не было в меню «Пуск»
        foreach (var win in openWindowEntries)
        {
            if (string.Equals(win.Name, "ZenithApp", StringComparison.OrdinalIgnoreCase)) continue;
            if (!seenNames.Add(win.Name)) continue;

            var entry = new NextSessionProgramEntry
            {
                Name = win.Name,
                ExecutablePath = win.ExePath,
                ProcessName = win.ProcessName
            };
            var key = entry.GetNormalizedKey();

            if (!itemsByKey.ContainsKey(key))
            {
                itemsByKey[key] = new NextSessionProgramItem
                {
                    Key = key,
                    Name = win.Name,
                    ExecutablePath = win.ExePath,
                    ProcessName = win.ProcessName,
                    Icon = win.Icon ?? ResolveIcon(win.ExePath, win.ProcessName),
                    IsRunning = true
                };
            }
        }

        return itemsByKey.Values
            .OrderByDescending(i => i.IsCustom)
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        void AddEntryIfValid(NextSessionProgramEntry entry, bool isCustom)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) && string.IsNullOrWhiteSpace(entry.ExecutablePath))
            {
                return;
            }

            var key = entry.GetNormalizedKey();
            if (itemsByKey.ContainsKey(key)) return;

            var displayName = !string.IsNullOrWhiteSpace(entry.Name)
                ? entry.Name.Trim()
                : Path.GetFileNameWithoutExtension(entry.ExecutablePath);

            seenNames.Add(displayName);
            itemsByKey[key] = new NextSessionProgramItem
            {
                Key = key,
                Name = displayName,
                ExecutablePath = entry.ExecutablePath,
                ProcessName = entry.ProcessName,
                Arguments = entry.Arguments,
                Icon = ResolveIcon(entry.ExecutablePath, entry.ProcessName),
                IsCustom = isCustom,
                IsRunning = IsMatchRunning(entry.ProcessName, entry.ExecutablePath, runningProcessNames, runningExePaths)
            };
        }
    }

    public ImageSource? ResolveIcon(string? executablePath, string? processName = null)
    {
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var icon = WindowListService.GetIconForPath(executablePath);
            if (icon != null) return icon;
        }

        if (!string.IsNullOrWhiteSpace(processName))
        {
            return WindowListService.GetIconForProcessName(processName);
        }

        return null;
    }

    private static void CollectRunningApplications(
        HashSet<string> runningProcessNames,
        HashSet<string> runningExePaths,
        List<(string Name, string ProcessName, string ExePath, ImageSource? Icon)> openWindowEntries)
    {
        try
        {
            var openWindows = WindowListService.GetOpenWindows(nint.Zero, maxCount: 60);
            foreach (var win in openWindows)
            {
                if (string.IsNullOrWhiteSpace(win.ProcessName)) continue;

                runningProcessNames.Add(win.ProcessName);
                if (!string.IsNullOrWhiteSpace(win.ExePath))
                {
                    runningExePaths.Add(win.ExePath);
                }

                openWindowEntries.Add((win.ProcessName, win.ProcessName, win.ExePath, win.Icon));
            }
        }
        catch
        {
            // Ignore window enumeration failure
        }

        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.Id == Environment.ProcessId || string.IsNullOrWhiteSpace(proc.MainWindowTitle))
                    {
                        continue;
                    }

                    runningProcessNames.Add(proc.ProcessName);
                    var exePath = proc.MainModule?.FileName ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(exePath))
                    {
                        runningExePaths.Add(exePath);
                    }

                    openWindowEntries.Add((proc.ProcessName, proc.ProcessName, exePath, null));
                }
                catch
                {
                    // Access denied for elevated/system processes
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            // Ignore process enumeration failure
        }
    }

    private static bool IsMatchRunning(
        string nameOrProcess,
        string executablePath,
        HashSet<string> runningProcessNames,
        HashSet<string> runningExePaths)
    {
        if (!string.IsNullOrWhiteSpace(executablePath) && runningExePaths.Contains(executablePath))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(nameOrProcess) && runningProcessNames.Contains(nameOrProcess))
        {
            return true;
        }

        var fileName = Path.GetFileNameWithoutExtension(executablePath);
        return !string.IsNullOrWhiteSpace(fileName) && runningProcessNames.Contains(fileName);
    }
}
