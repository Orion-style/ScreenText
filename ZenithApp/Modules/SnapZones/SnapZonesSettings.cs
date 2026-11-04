using System;
using System.Collections.Generic;
using System.Windows.Input;
using ZenithApp.Core.Hotkeys;

namespace ZenithApp.Modules.SnapZones;

public sealed class SnapZonesSettings
{
    // Hotkey: default Ctrl + Win + T
    public KeyModifiers HotkeyModifiers { get; set; } = KeyModifiers.Control | KeyModifiers.Win;
    public Key HotkeyKey { get; set; } = Key.T;

    // Visual border around pinned window
    public bool ShowBorder { get; set; } = true;
    public int BorderThickness { get; set; } = 3;
    public string BorderColorHex { get; set; } = "#0078D7"; // Fluent Accent Blue
    public bool PlayNotificationSound { get; set; } = true;

    // Excluded processes that should not be pinned
    public List<string> ExcludedProcesses { get; set; } = new()
    {
        "dwm", "explorer", "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost", "ZenithApp"
    };
}
