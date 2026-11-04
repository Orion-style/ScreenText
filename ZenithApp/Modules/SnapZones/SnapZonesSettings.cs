using System;
using System.Collections.Generic;

namespace ZenithApp.Modules.SnapZones;

public enum SnapTriggerMode
{
    ShiftDrag = 0, // Hold Shift while dragging (default)
    AlwaysOnDrag = 1 // Always show zones while dragging
}

public enum ZoneLayoutPreset
{
    TwoColumns5050 = 0,
    ThreeColumns = 1,
    TwoColumns6633 = 2,
    TwoColumns3366 = 3,
    TwoRows = 4,
    Grid2x2 = 5,
    PriorityGrid = 6, // 50 / 25 / 25
    CenterSingle = 7,
    Custom = 8
}

public sealed class CustomZoneDefinition
{
    public double XPercent { get; set; }
    public double YPercent { get; set; }
    public double WidthPercent { get; set; }
    public double HeightPercent { get; set; }
}

public sealed class CustomLayoutProfile
{
    public string Name { get; set; } = "Пользовательская";
    public int Columns { get; set; } = 2;
    public int Rows { get; set; } = 1;
    // Normalized column and row ratios, e.g. [1, 1] or [2, 1]
    public List<double> ColumnWeights { get; set; } = new() { 1, 1 };
    public List<double> RowWeights { get; set; } = new() { 1 };
}

public sealed class SnapZonesSettings
{
    public SnapTriggerMode TriggerMode { get; set; } = SnapTriggerMode.ShiftDrag;
    public ZoneLayoutPreset DefaultPreset { get; set; } = ZoneLayoutPreset.TwoColumns5050;
    
    // Per-monitor preset mapping: Monitor DeviceName -> Preset Name
    public Dictionary<string, string> MonitorPresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int ZoneSpacing { get; set; } = 8;
    public int ScreenMargin { get; set; } = 8;
    public bool RestoreOriginalSizeOnUnsnap { get; set; } = true;

    // List of process names to ignore (e.g., games, fullscreens)
    public List<string> ExcludedProcesses { get; set; } = new()
    {
        "dwm", "explorer", "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost", "ZenithApp"
    };

    // Custom user layouts
    public List<CustomLayoutProfile> CustomLayouts { get; set; } = new()
    {
        new CustomLayoutProfile
        {
            Name = "Своя 3 колонки (1:2:1)",
            Columns = 3,
            Rows = 1,
            ColumnWeights = new List<double> { 1, 2, 1 },
            RowWeights = new List<double> { 1 }
        }
    };
}
