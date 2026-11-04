using System.Collections.Generic;
using System.Windows.Input;
using ZenithApp.Core.Hotkeys;

namespace ZenithApp.Modules.T9Predictor;

public enum AcceptKeyOption
{
    Tab = 0,
    RightArrow = 1,
    Enter = 2
}

public sealed class T9PredictorSettings
{
    public bool EnableRussian { get; set; } = true;
    public bool EnableEnglish { get; set; } = true;

    public int MinPrefixLength { get; set; } = 2;
    public double HintOpacity { get; set; } = 0.50; // 50% opacity
    public AcceptKeyOption AcceptKey { get; set; } = AcceptKeyOption.Tab;
    public bool AddSpaceAfterWord { get; set; } = false;

    // Hotkey for Pause / Resume
    public KeyModifiers PauseHotkeyModifiers { get; set; } = KeyModifiers.Control | KeyModifiers.Alt;
    public Key PauseHotkeyKey { get; set; } = Key.P;

    // Excluded applications by process name
    public List<string> ExcludedProcesses { get; set; } = new()
    {
        "1Password", "KeePass", "KeePassXC", "Bitwarden", "LastPass", "Enpass",
        "cmd", "powershell", "pwsh", "conhost", "WindowsTerminal"
    };
}
