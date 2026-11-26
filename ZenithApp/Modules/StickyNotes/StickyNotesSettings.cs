using System.Windows.Input;
using ZenithApp.Core.Hotkeys;

namespace ZenithApp.Modules.StickyNotes;

public sealed class StickyNotesSettings
{
    public KeyModifiers CreateHotkeyModifiers { get; set; } = KeyModifiers.Control | KeyModifiers.Alt;
    public Key CreateHotkeyKey { get; set; } = Key.S;

    public KeyModifiers ToggleVisibilityHotkeyModifiers { get; set; } = KeyModifiers.Control | KeyModifiers.Alt;
    public Key ToggleVisibilityHotkeyKey { get; set; } = Key.H;

    public bool AllNotesHidden { get; set; } = false;
    public string DefaultColorHex { get; set; } = "graphite";
    public double DefaultOpacity { get; set; } = 0.92;
    public double DefaultFontSize { get; set; } = 14;
    public bool DefaultTopmost { get; set; } = true;
}
