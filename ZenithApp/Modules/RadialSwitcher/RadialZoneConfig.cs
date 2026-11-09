namespace ZenithApp.Modules.RadialSwitcher;

public sealed class RadialZoneConfig
{
    public int Index { get; set; }
    public string DirectionName { get; set; } = string.Empty;
    public bool IsPinned { get; set; } = false;
    public string? PinnedProcessName { get; set; }
    public string? CustomLabel { get; set; }

    public RadialZoneConfig() { }

    public RadialZoneConfig(int index, string directionName)
    {
        Index = index;
        DirectionName = directionName;
    }
}
