using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
                Publisher = publisher
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
                Publisher = publisher
            });
        }
    }

    public bool SetStartupEntryEnabled(StartupEntry entry, bool enable)
    {
        try
        {
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
            if (entry.Location == StartupEntryLocation.CurrentUserRegistry)
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
