namespace ZenithApp.Modules.DynamicIsland;

public sealed class DynamicIslandSettings
{
    public bool ShowAppNotifications { get; set; } = true;
    public bool ShowNextSessionCountdown { get; set; } = true;
    public bool ShowMediaControls { get; set; } = true;
    public bool AutoHideWhenIdle { get; set; } = false;
    public bool HideInFullscreen { get; set; } = true;
    public bool RemoveShelfItemAfterDrag { get; set; } = false;
    public int TopMarginPx { get; set; } = 0;
}
