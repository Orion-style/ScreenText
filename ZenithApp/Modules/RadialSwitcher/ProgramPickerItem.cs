using System.Windows.Media;

namespace ZenithApp.Modules.RadialSwitcher;

public sealed class ProgramPickerItem
{
    public string Name { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string ExePath { get; init; } = string.Empty;
    public ImageSource? Icon { get; init; }

    public string Subtitle => !string.IsNullOrWhiteSpace(ExePath) ? ExePath : ProcessName;
}
