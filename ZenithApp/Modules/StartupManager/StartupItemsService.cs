using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using ZenithApp.Core.Logging;

namespace ZenithApp.Modules.StartupManager;

public interface IStartupItemsService
{
    IReadOnlyList<StartupEntry> GetStartupEntries();
    bool SetStartupEntryEnabled(StartupEntry entry, bool enable);
    bool RemoveStartupEntry(StartupEntry entry);
}

public sealed class StartupItemsService : IStartupItemsService
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string StartupApprovedStartupFolderSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    private const string UninstallSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallWow64SubKey = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string AppPathsSubKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths";

    private readonly IAppLogger _logger;

    public StartupItemsService(IAppLogger logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<StartupEntry> GetStartupEntries()
    {
        var result = new List<StartupEntry>();

        try
        {
            ReadRegistryRunEntries(Registry.CurrentUser, StartupEntryLocation.CurrentUserRegistry, result);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to read HKCU Run entries", ex);
        }

        try
        {
            ReadRegistryRunEntries(Registry.LocalMachine, StartupEntryLocation.LocalMachineRegistry, result);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to read HKLM Run entries", ex);
        }

        try
        {
            var userStartupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            ReadFolderEntries(userStartupPath, StartupEntryLocation.StartupFolderUser, result);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to read User Startup folder entries", ex);
        }

        try
        {
            var commonStartupPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            ReadFolderEntries(commonStartupPath, StartupEntryLocation.StartupFolderCommon, result);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to read Common Startup folder entries", ex);
        }

        // Также загружаем установленные программы, которые потенциально могут быть в автозагрузке
        try
        {
            ReadInstalledApplications(result);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to read installed applications for potential startup", ex);
        }

        return result;
    }

    private void ReadRegistryRunEntries(RegistryKey rootKey, StartupEntryLocation location, List<StartupEntry> list)
    {
        using var runKey = rootKey.OpenSubKey(RunSubKey, writable: false);
        if (runKey == null) return;

        using var approvedKey = rootKey.OpenSubKey(StartupApprovedRunSubKey, writable: false);

        foreach (var valueName in runKey.GetValueNames())
        {
            if (string.IsNullOrWhiteSpace(valueName)) continue;

            var cmd = runKey.GetValue(valueName)?.ToString() ?? string.Empty;
            var isEnabled = true;

            if (approvedKey != null)
            {
                var approvedData = approvedKey.GetValue(valueName) as byte[];
                if (approvedData != null && approvedData.Length > 0)
                {
                    // Even values (typically 0x02, 0x04, 0x06...) mean enabled, odd values (typically 0x03) mean disabled
                    isEnabled = (approvedData[0] % 2 == 0);
                }
            }

            var exePath = ExtractExecutablePath(cmd);
            var publisher = GetPublisher(exePath);

            list.Add(new StartupEntry
            {
                Id = $"{location}:{valueName}",
                Name = valueName,
                Command = cmd,
                Location = location,
                FilePath = exePath,
                IsEnabled = isEnabled,
                Publisher = publisher,
                IsConfiguredInStartup = true
            });
        }
    }

    private void ReadFolderEntries(string folderPath, StartupEntryLocation location, List<StartupEntry> list)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

        using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedStartupFolderSubKey, writable: false);

        var files = Directory.GetFiles(folderPath);
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

            var name = Path.GetFileNameWithoutExtension(file);
            var isEnabled = true;

            if (approvedKey != null)
            {
                var approvedData = approvedKey.GetValue(fileName) as byte[];
                if (approvedData != null && approvedData.Length > 0)
                {
                    isEnabled = (approvedData[0] % 2 == 0);
                }
            }

            var exePath = file;
            var publisher = GetPublisher(exePath);

            list.Add(new StartupEntry
            {
                Id = $"{location}:{fileName}",
                Name = name,
                Command = file,
                Location = location,
                FilePath = exePath,
                IsEnabled = isEnabled,
                Publisher = publisher,
                IsConfiguredInStartup = true
            });
        }
    }

    private void ReadInstalledApplications(List<StartupEntry> list)
    {
        // Словарь путей/имен уже зарегистрированных в автозагрузке
        var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in list)
        {
            if (!string.IsNullOrWhiteSpace(entry.FilePath))
            {
                existingPaths.Add(entry.FilePath.Trim('"', ' '));
            }
            if (!string.IsNullOrWhiteSpace(entry.Name))
            {
                existingNames.Add(entry.Name.Trim());
            }
        }

        // Кэш App Paths для быстрого нахождения exe по имени
        var appPathsMap = LoadAppPaths();

        // Считываем установленные приложения из HKCU и HKLM
        var installedApps = new List<InstalledAppInfo>();
        ReadUninstallKey(Registry.CurrentUser, UninstallSubKey, installedApps);
        ReadUninstallKey(Registry.LocalMachine, UninstallSubKey, installedApps);
        ReadUninstallKey(Registry.LocalMachine, UninstallWow64SubKey, installedApps);

        var addedAppNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in installedApps)
        {
            if (string.IsNullOrWhiteSpace(app.DisplayName)) continue;

            // Пропускаем обновления, системные компоненты или родительские пакеты
            if (app.IsSystemComponent) continue;

            var cleanName = CleanProgramDisplayName(app.DisplayName);
            if (addedAppNames.Contains(cleanName) || existingNames.Contains(cleanName)) continue;

            var exePath = ResolveApplicationExecutable(app, appPathsMap);
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                continue;
            }

            if (existingPaths.Contains(exePath))
            {
                continue;
            }

            addedAppNames.Add(cleanName);
            existingPaths.Add(exePath);

            var publisher = !string.IsNullOrWhiteSpace(app.Publisher) ? app.Publisher : GetPublisher(exePath);

            list.Add(new StartupEntry
            {
                Id = $"installed:{cleanName}",
                Name = cleanName,
                Command = $"\"{exePath}\"",
                Location = StartupEntryLocation.InstalledProgram,
                FilePath = exePath,
                IsEnabled = false,
                Publisher = publisher,
                IsConfiguredInStartup = false
            });
        }
    }

    private static string CleanProgramDisplayName(string name)
    {
        var result = name.Trim();
        var commaIdx = result.IndexOf(',', StringComparison.Ordinal);
        if (commaIdx > 0 && (result.Contains("версия", StringComparison.OrdinalIgnoreCase) || result.Contains("version", StringComparison.OrdinalIgnoreCase)))
        {
            result = result.Substring(0, commaIdx).Trim();
        }
        return result;
    }

    private sealed class InstalledAppInfo
    {
        public string DisplayName { get; set; } = string.Empty;
        public string? Publisher { get; set; }
        public string? DisplayIcon { get; set; }
        public string? InstallLocation { get; set; }
        public string? UninstallString { get; set; }
        public bool IsSystemComponent { get; set; }
    }

    private static void ReadUninstallKey(RegistryKey rootKey, string subKeyPath, List<InstalledAppInfo> apps)
    {
        using var key = rootKey.OpenSubKey(subKeyPath, writable: false);
        if (key == null) return;

        foreach (var subKeyName in key.GetSubKeyNames())
        {
            using var subKey = key.OpenSubKey(subKeyName, writable: false);
            if (subKey == null) continue;

            var displayName = subKey.GetValue("DisplayName")?.ToString();
            if (string.IsNullOrWhiteSpace(displayName)) continue;

            // Фильтрация системных компонентов и обновлений
            var systemComponent = subKey.GetValue("SystemComponent");
            if (systemComponent != null && (Convert.ToInt32(systemComponent) == 1)) continue;

            var parentKeyName = subKey.GetValue("ParentKeyName")?.ToString();
            if (!string.IsNullOrEmpty(parentKeyName)) continue;

            var releaseType = subKey.GetValue("ReleaseType")?.ToString();
            if (string.Equals(releaseType, "Security Update", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(releaseType, "Update", StringComparison.OrdinalIgnoreCase)) continue;

            apps.Add(new InstalledAppInfo
            {
                DisplayName = displayName,
                Publisher = subKey.GetValue("Publisher")?.ToString(),
                DisplayIcon = subKey.GetValue("DisplayIcon")?.ToString(),
                InstallLocation = subKey.GetValue("InstallLocation")?.ToString(),
                UninstallString = subKey.GetValue("UninstallString")?.ToString(),
                IsSystemComponent = false
            });
        }
    }

    private static Dictionary<string, string> LoadAppPaths()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void ReadFromKey(RegistryKey rootKey)
        {
            using var appPathsKey = rootKey.OpenSubKey(AppPathsSubKey, writable: false);
            if (appPathsKey == null) return;

            foreach (var name in appPathsKey.GetSubKeyNames())
            {
                using var exeKey = appPathsKey.OpenSubKey(name, writable: false);
                var path = exeKey?.GetValue(null)?.ToString();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var clean = path.Trim('"', ' ');
                    if (File.Exists(clean))
                    {
                        map[name] = clean;
                        var nameWithoutExt = Path.GetFileNameWithoutExtension(name);
                        if (!map.ContainsKey(nameWithoutExt))
                        {
                            map[nameWithoutExt] = clean;
                        }
                    }
                }
            }
        }

        try
        {
            ReadFromKey(Registry.CurrentUser);
            ReadFromKey(Registry.LocalMachine);
        }
        catch
        {
            // Ignore registry read errors for App Paths
        }

        return map;
    }

    private static string? ResolveApplicationExecutable(InstalledAppInfo app, Dictionary<string, string> appPathsMap)
    {
        // 1. Проверяем DisplayIcon
        if (!string.IsNullOrWhiteSpace(app.DisplayIcon))
        {
            var rawIcon = app.DisplayIcon.Trim();
            // DisplayIcon часто имеет вид: "C:\Path\App.exe,0" или "C:\Path\App.ico"
            var commaIdx = rawIcon.LastIndexOf(',');
            var iconCandidate = (commaIdx > 0 && commaIdx > rawIcon.LastIndexOf('.'))
                ? rawIcon.Substring(0, commaIdx).Trim('"', ' ')
                : rawIcon.Trim('"', ' ');

            if (iconCandidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(iconCandidate))
            {
                var iconFileName = Path.GetFileName(iconCandidate);
                // Исключаем uninstall/setup инсталляторы
                if (!iconFileName.Contains("unins", StringComparison.OrdinalIgnoreCase) &&
                    !iconFileName.Contains("setup", StringComparison.OrdinalIgnoreCase))
                {
                    return iconCandidate;
                }
            }
        }

        // 2. Проверяем InstallLocation
        if (!string.IsNullOrWhiteSpace(app.InstallLocation))
        {
            var installDir = app.InstallLocation.Trim('"', ' ');
            if (Directory.Exists(installDir))
            {
                try
                {
                    var files = Directory.GetFiles(installDir, "*.exe", SearchOption.TopDirectoryOnly);
                    var candidates = files.Where(f =>
                    {
                        var name = Path.GetFileName(f);
                        return !name.Contains("unins", StringComparison.OrdinalIgnoreCase) &&
                               !name.Contains("setup", StringComparison.OrdinalIgnoreCase) &&
                               !name.Contains("update", StringComparison.OrdinalIgnoreCase);
                    }).ToList();

                    if (candidates.Count > 0)
                    {
                        // Предпочитаем исполняемый файл, название которого совпадает или похоже на DisplayName
                        var bestMatch = candidates.FirstOrDefault(c =>
                            app.DisplayName.Contains(Path.GetFileNameWithoutExtension(c), StringComparison.OrdinalIgnoreCase) ||
                            Path.GetFileNameWithoutExtension(c).Contains(CleanProgramDisplayName(app.DisplayName), StringComparison.OrdinalIgnoreCase));

                        return bestMatch ?? candidates[0];
                    }
                }
                catch
                {
                    // Ignore directory access errors
                }
            }
        }

        // 3. Проверяем App Paths
        var shortName = CleanProgramDisplayName(app.DisplayName).Replace(" ", "");
        if (appPathsMap.TryGetValue(shortName, out var foundExe))
        {
            return foundExe;
        }

        return null;
    }

    public bool SetStartupEntryEnabled(StartupEntry entry, bool enable)
    {
        try
        {
            // Если это установленная программа, которая еще не была зарегистрирована в Run
            if (entry.Location == StartupEntryLocation.InstalledProgram || !entry.IsConfiguredInStartup)
            {
                if (enable)
                {
                    using var runKey = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: true);
                    if (runKey == null)
                    {
                        _logger.LogWarning("Unable to open HKCU Run subkey.");
                        return false;
                    }

                    runKey.SetValue(entry.Name, entry.Command, RegistryValueKind.String);

                    // Убеждаемся что в StartupApproved он активен
                    using var approvedKey = Registry.CurrentUser.CreateSubKey(StartupApprovedRunSubKey, writable: true);
                    if (approvedKey != null)
                    {
                        var initBuffer = new byte[12];
                        initBuffer[0] = 0x02; // enabled
                        var ft = DateTime.UtcNow.ToFileTime();
                        var tb = BitConverter.GetBytes(ft);
                        Array.Copy(tb, 0, initBuffer, 4, Math.Min(tb.Length, 8));
                        approvedKey.SetValue(entry.Name, initBuffer, RegistryValueKind.Binary);
                    }

                    entry.IsEnabled = true;
                    entry.IsConfiguredInStartup = true;
                    _logger.LogInfo($"Installed program '{entry.Name}' added to HKCU Run and enabled.");
                    return true;
                }
                else
                {
                    // Уже выключена
                    entry.IsEnabled = false;
                    return true;
                }
            }

            RegistryKey? rootKey = entry.Location switch
            {
                StartupEntryLocation.CurrentUserRegistry => Registry.CurrentUser,
                StartupEntryLocation.LocalMachineRegistry => Registry.LocalMachine,
                StartupEntryLocation.StartupFolderUser => Registry.CurrentUser,
                StartupEntryLocation.StartupFolderCommon => Registry.CurrentUser,
                _ => null
            };

            if (rootKey == null) return false;

            string approvedSubKey = (entry.Location == StartupEntryLocation.CurrentUserRegistry || entry.Location == StartupEntryLocation.LocalMachineRegistry)
                ? StartupApprovedRunSubKey
                : StartupApprovedStartupFolderSubKey;

            using var key = rootKey.CreateSubKey(approvedSubKey, writable: true);
            if (key == null)
            {
                _logger.LogWarning($"Unable to open StartupApproved subkey '{approvedSubKey}'.");
                return false;
            }

            var itemName = (entry.Location == StartupEntryLocation.StartupFolderUser || entry.Location == StartupEntryLocation.StartupFolderCommon)
                ? Path.GetFileName(entry.Command)
                : entry.Name;

            var existing = key.GetValue(itemName) as byte[];
            byte[] buffer;
            if (existing != null && existing.Length >= 12)
            {
                buffer = (byte[])existing.Clone();
            }
            else
            {
                buffer = new byte[12];
            }

            // In Windows StartupApproved:
            // First byte: 0x02 = enabled, 0x03 = disabled
            buffer[0] = enable ? (byte)0x02 : (byte)0x03;

            // Timestamp in 64-bit FILETIME format starting at byte 4 (standard Windows format)
            var fileTime = DateTime.UtcNow.ToFileTime();
            var timeBytes = BitConverter.GetBytes(fileTime);
            Array.Copy(timeBytes, 0, buffer, 4, Math.Min(timeBytes.Length, 8));

            key.SetValue(itemName, buffer, RegistryValueKind.Binary);
            entry.IsEnabled = enable;
            _logger.LogInfo($"Startup entry '{entry.Name}' enabled state changed to {enable}.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to change startup entry '{entry.Name}' enabled state to {enable}.", ex);
            return false;
        }
    }

    public bool RemoveStartupEntry(StartupEntry entry)
    {
        try
        {
            if (entry.Location == StartupEntryLocation.InstalledProgram || !entry.IsConfiguredInStartup)
            {
                // Для потенциальных программ удаление означает просто выключение/снятие из реестра если было добавлено
                using var runKey = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: true);
                runKey?.DeleteValue(entry.Name, throwOnMissingValue: false);

                using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedRunSubKey, writable: true);
                approvedKey?.DeleteValue(entry.Name, throwOnMissingValue: false);

                entry.IsEnabled = false;
                entry.IsConfiguredInStartup = false;
                return true;
            }
            else if (entry.Location == StartupEntryLocation.CurrentUserRegistry)
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: true);
                runKey?.DeleteValue(entry.Name, throwOnMissingValue: false);

                using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedRunSubKey, writable: true);
                approvedKey?.DeleteValue(entry.Name, throwOnMissingValue: false);
                return true;
            }
            else if (entry.Location == StartupEntryLocation.LocalMachineRegistry)
            {
                using var runKey = Registry.LocalMachine.OpenSubKey(RunSubKey, writable: true);
                runKey?.DeleteValue(entry.Name, throwOnMissingValue: false);

                using var approvedKey = Registry.LocalMachine.OpenSubKey(StartupApprovedRunSubKey, writable: true);
                approvedKey?.DeleteValue(entry.Name, throwOnMissingValue: false);
                return true;
            }
            else if (entry.Location == StartupEntryLocation.StartupFolderUser || entry.Location == StartupEntryLocation.StartupFolderCommon)
            {
                if (File.Exists(entry.Command))
                {
                    File.Delete(entry.Command);
                }

                using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedStartupFolderSubKey, writable: true);
                approvedKey?.DeleteValue(Path.GetFileName(entry.Command), throwOnMissingValue: false);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to delete startup entry '{entry.Name}'.", ex);
            return false;
        }
    }

    private static string? ExtractExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        var trimmed = command.Trim();
        if (trimmed.StartsWith("\""))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                return trimmed.Substring(1, closingQuote - 1);
            }
        }

        var spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex > 0)
        {
            return trimmed.Substring(0, spaceIndex);
        }

        return trimmed;
    }

    private static string? GetPublisher(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
            return !string.IsNullOrWhiteSpace(versionInfo.CompanyName) 
                ? versionInfo.CompanyName 
                : versionInfo.ProductName;
        }
        catch
        {
            return null;
        }
    }
}
