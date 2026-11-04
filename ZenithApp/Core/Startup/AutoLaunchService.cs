using System.Diagnostics;
using Microsoft.Win32;
using ZenithApp.Core.Logging;

namespace ZenithApp.Core.Startup;

public interface IAutoLaunchService
{
    bool IsAutoLaunchEnabled();
    bool SetAutoLaunch(bool enable);
}

public sealed class AutoLaunchService : IAutoLaunchService
{
    private const string RunRegistrySubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryValueName = "ZenithApp";
    private readonly IAppLogger _logger;

    public AutoLaunchService(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsAutoLaunchEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistrySubKey, writable: false);
            var value = key?.GetValue(AppRegistryValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
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
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistrySubKey, writable: true);
            if (key == null)
            {
                _logger.LogWarning("Unable to open HKCU Run registry key with write access.");
                return false;
            }

            if (enable)
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    _logger.LogWarning("Current process executable path is null.");
                    return false;
                }

                // Append argument to start silently in tray if needed
                var command = $"\"{exePath}\" --minimized";
                key.SetValue(AppRegistryValueName, command);
                _logger.LogInfo($"Auto-launch registered: {command}");
            }
            else
            {
                key.DeleteValue(AppRegistryValueName, throwOnMissingValue: false);
                _logger.LogInfo("Auto-launch removed from registry.");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to update auto-launch (enable: {enable}) in HKCU Run registry.", ex);
            return false;
        }
    }
}
