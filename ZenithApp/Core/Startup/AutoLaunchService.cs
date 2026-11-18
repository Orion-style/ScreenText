using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using ZenithApp.Core.Logging;

namespace ZenithApp.Core.Startup;

public interface IAutoLaunchService
{
    bool IsAutoLaunchEnabled();
    bool SetAutoLaunch(bool enable);
    event Action<bool>? AutoLaunchChanged;
    void NotifyAutoLaunchChanged(bool enable);
}

public sealed class AutoLaunchService : IAutoLaunchService
{
    private const string RunRegistrySubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private static readonly string[] PossibleValueNames = { "ZenithApp", "ZenithUtility" };
    private const string PrimaryRegistryValueName = "ZenithApp";
    private readonly IAppLogger _logger;

    public event Action<bool>? AutoLaunchChanged;

    public AutoLaunchService(IAppLogger logger)
    {
        _logger = logger;
    }

    public void NotifyAutoLaunchChanged(bool enable)
    {
        AutoLaunchChanged?.Invoke(enable);
    }

    public bool IsAutoLaunchEnabled()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistrySubKey, writable: false);
            if (runKey == null) return false;

            string? activeValueName = null;
            string? command = null;

            foreach (var name in PossibleValueNames)
            {
                var val = runKey.GetValue(name) as string;
                if (!string.IsNullOrWhiteSpace(val))
                {
                    activeValueName = name;
                    command = val;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(command) || activeValueName == null)
            {
                return false;
            }

            // Verify executable path exists if possible
            var exePath = ExtractExePath(command);
            if (!string.IsNullOrEmpty(exePath) && !File.Exists(exePath))
            {
                _logger.LogWarning($"Auto-launch command points to non-existent executable: {exePath}");
                return false;
            }

            // Check StartupApproved\Run
            using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedRunSubKey, writable: false);
            if (approvedKey != null)
            {
                var approvedData = approvedKey.GetValue(activeValueName) as byte[];
                if (approvedData != null && approvedData.Length > 0)
                {
                    // Even values (typically 0x02, 0x04...) mean enabled, odd values (typically 0x03) mean disabled
                    return (approvedData[0] % 2 == 0);
                }
            }

            // If not present in StartupApproved, it is enabled by default by Windows
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to check auto-launch status in HKCU Run registry.", ex);
            return false;
        }
    }

    public bool SetAutoLaunch(bool enable)
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistrySubKey, writable: true);
            if (runKey == null)
            {
                _logger.LogWarning("Unable to open HKCU Run registry key with write access.");
                return false;
            }

            using var approvedKey = Registry.CurrentUser.CreateSubKey(StartupApprovedRunSubKey, writable: true);

            if (enable)
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    _logger.LogWarning("Current process executable path is null.");
                    return false;
                }

                var command = $"\"{exePath}\" --minimized";
                runKey.SetValue(PrimaryRegistryValueName, command);

                // Clean up alternative value name if present
                if (runKey.GetValue("ZenithUtility") != null)
                {
                    runKey.DeleteValue("ZenithUtility", throwOnMissingValue: false);
                }

                // Explicitly approve in StartupApproved\Run (byte 0 = 0x02)
                if (approvedKey != null)
                {
                    var existing = approvedKey.GetValue(PrimaryRegistryValueName) as byte[];
                    byte[] buffer = (existing != null && existing.Length >= 12) ? (byte[])existing.Clone() : new byte[12];
                    buffer[0] = 0x02; // enabled
                    var fileTime = DateTime.UtcNow.ToFileTime();
                    var timeBytes = BitConverter.GetBytes(fileTime);
                    Array.Copy(timeBytes, 0, buffer, 4, Math.Min(timeBytes.Length, 8));

                    approvedKey.SetValue(PrimaryRegistryValueName, buffer, RegistryValueKind.Binary);
                    approvedKey.DeleteValue("ZenithUtility", throwOnMissingValue: false);
                }

                _logger.LogInfo($"Auto-launch registered and approved: {command}");
            }
            else
            {
                // Remove from Run key
                runKey.DeleteValue(PrimaryRegistryValueName, throwOnMissingValue: false);
                runKey.DeleteValue("ZenithUtility", throwOnMissingValue: false);

                // Mark disabled or remove from StartupApproved\Run
                if (approvedKey != null)
                {
                    var existing = approvedKey.GetValue(PrimaryRegistryValueName) as byte[];
                    if (existing != null && existing.Length >= 12)
                    {
                        byte[] buffer = (byte[])existing.Clone();
                        buffer[0] = 0x03; // disabled
                        var fileTime = DateTime.UtcNow.ToFileTime();
                        var timeBytes = BitConverter.GetBytes(fileTime);
                        Array.Copy(timeBytes, 0, buffer, 4, Math.Min(timeBytes.Length, 8));
                        approvedKey.SetValue(PrimaryRegistryValueName, buffer, RegistryValueKind.Binary);
                    }
                    else
                    {
                        approvedKey.DeleteValue(PrimaryRegistryValueName, throwOnMissingValue: false);
                    }
                    approvedKey.DeleteValue("ZenithUtility", throwOnMissingValue: false);
                }

                _logger.LogInfo("Auto-launch removed from registry.");
            }

            NotifyAutoLaunchChanged(enable);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to update auto-launch (enable: {enable}) in HKCU Run registry.", ex);
            return false;
        }
    }

    private static string ExtractExePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return string.Empty;
        var trimmed = command.Trim();
        if (trimmed.StartsWith("\""))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote > 0)
            {
                return trimmed.Substring(1, closingQuote - 1);
            }
        }
        var spaceIdx = trimmed.IndexOf(' ');
        return spaceIdx > 0 ? trimmed.Substring(0, spaceIdx) : trimmed;
    }
}
