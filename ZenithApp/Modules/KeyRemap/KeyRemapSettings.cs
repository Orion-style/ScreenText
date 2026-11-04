namespace ZenithApp.Modules.KeyRemap;

public sealed class KeyRemapSettings
{
    public bool EmergencyEscapeEnabled { get; set; } = true;
    public string EmergencyShortcutText { get; set; } = "Ctrl + Shift + Esc (удержание 1.5 сек)";
    public int EmergencyHoldDurationMs { get; set; } = 1500;

    public List<KeyRemapRule> Rules { get; set; } = new();
}
