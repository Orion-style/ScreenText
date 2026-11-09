using System.Windows.Media;

namespace ZenithApp.Modules.RadialSwitcher;

public sealed class WindowInfo
{
    public nint Hwnd { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string ExePath { get; init; } = string.Empty;
    public ImageSource? Icon { get; init; }
    public bool IsActive { get; init; }
}
