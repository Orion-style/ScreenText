namespace ZenithApp.Core.Settings;

public enum AppThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; } = false;
    public AppThemeMode Theme { get; set; } = AppThemeMode.System;
    public Dictionary<string, bool> EnabledModules { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["screenshot"] = true,
        ["smart_links"] = true
    };
    public ZenithApp.Modules.Screenshot.ScreenshotSettings Screenshot { get; set; } = new();
    public ZenithApp.Modules.SmartLinks.SmartLinksSettings SmartLinks { get; set; } = new();
    public ZenithApp.Modules.KeyRemap.KeyRemapSettings KeyRemap { get; set; } = new();
    public ZenithApp.Modules.QuickLook.QuickLookSettings QuickLook { get; set; } = new();
    public ZenithApp.Modules.SnapZones.SnapZonesSettings SnapZones { get; set; } = new();

    public bool IsModuleEnabled(string moduleId, bool defaultValue = false)
    {
        if (EnabledModules.TryGetValue(moduleId, out var enabled))
        {
            return enabled;
        }

        // По умолчанию базовые модули скриншота и подсказок включены
        if (string.Equals(moduleId, "screenshot", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(moduleId, "smart_links", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return defaultValue;
    }

    public void SetModuleEnabled(string moduleId, bool enabled)
    {
        EnabledModules[moduleId] = enabled;
    }
}
