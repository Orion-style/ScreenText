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
    public Dictionary<string, bool> EnabledModules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ZenithApp.Modules.Screenshot.ScreenshotSettings Screenshot { get; set; } = new();
    public ZenithApp.Modules.SmartLinks.SmartLinksSettings SmartLinks { get; set; } = new();
    public ZenithApp.Modules.KeyRemap.KeyRemapSettings KeyRemap { get; set; } = new();
    public ZenithApp.Modules.QuickLook.QuickLookSettings QuickLook { get; set; } = new();

    public bool IsModuleEnabled(string moduleId, bool defaultValue = false)
    {
        return EnabledModules.TryGetValue(moduleId, out var enabled) ? enabled : defaultValue;
    }

    public void SetModuleEnabled(string moduleId, bool enabled)
    {
        EnabledModules[moduleId] = enabled;
    }
}
