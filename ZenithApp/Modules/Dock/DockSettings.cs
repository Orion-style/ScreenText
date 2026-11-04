namespace ZenithApp.Modules.Dock;

public enum DockMonitorTarget
{
    Primary = 0,
    Cursor = 1
}

public sealed class DockItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty; // .exe, .lnk, directory, or file path
    public string Arguments { get; set; } = string.Empty;
    public string CustomIconPath { get; set; } = string.Empty;
    public bool IsPinned { get; set; } = true;
}

public sealed class DockSettings
{
    public bool AutoHide { get; set; } = true;
    public int BottomMargin { get; set; } = 12; // pixels above taskbar or screen bottom
    public double IconSize { get; set; } = 48; // default icon base size (px)
    public bool EnableMagnification { get; set; } = true;
    public double MagnificationFactor { get; set; } = 1.35; // max zoom factor under cursor
    public bool EnableBounceAnimation { get; set; } = true;
    public DockMonitorTarget MonitorTarget { get; set; } = DockMonitorTarget.Primary;

    // Hotkey for toggle: Ctrl + Alt + D
    public string HotkeyModifiers { get; set; } = "Control, Alt";
    public string HotkeyKey { get; set; } = "D";

    public List<DockItem> Items { get; set; } = new();
}
