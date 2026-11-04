namespace ZenithApp.Modules.QuickLook;

public sealed class QuickLookSettings
{
    public bool AutoPlayMedia { get; set; } = true;
    public int MaxTextFileSizeKb { get; set; } = 1024; // 1 MB default
    public int MaxWindowScreenPercent { get; set; } = 80; // 80% default

    // Format category switches
    public bool EnableImages { get; set; } = true;
    public bool EnablePdf { get; set; } = true;
    public bool EnableMedia { get; set; } = true; // Video & Audio
    public bool EnableText { get; set; } = true;
}
