namespace ZenithApp.Modules.KeyRemap;

public enum KeyRemapActionType
{
    RemapKey = 0,             // Single key -> Single key (e.g. CapsLock -> Backspace)
    RemapKeyCombination = 1,  // Key or Combo -> Key Combination (e.g. CapsLock -> Ctrl+C)
    DisableKey = 2            // Key -> Disabled (suppressed, does nothing)
}

public sealed class KeyRemapRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    // Trigger
    public int TriggerVirtualKey { get; set; }
    public bool TriggerCtrl { get; set; }
    public bool TriggerAlt { get; set; }
    public bool TriggerShift { get; set; }
    public bool TriggerWin { get; set; }

    // Action
    public KeyRemapActionType ActionType { get; set; } = KeyRemapActionType.RemapKey;

    // Target (if RemapKey or RemapKeyCombination)
    public int TargetVirtualKey { get; set; }
    public bool TargetCtrl { get; set; }
    public bool TargetAlt { get; set; }
    public bool TargetShift { get; set; }
    public bool TargetWin { get; set; }

    // Target secondary keys (optional future expansion)
    public List<int> TargetExtraKeys { get; set; } = new();

    // Application filter (foundation for per-app rules)
    public bool EnableAppFilter { get; set; } = false;
    public bool AppFilterIsWhitelist { get; set; } = true; // true = only in these apps, false = exclude these apps
    public List<string> FilterProcesses { get; set; } = new();

    public KeyRemapRule Clone()
    {
        return new KeyRemapRule
        {
            Id = Id,
            Name = Name,
            IsEnabled = IsEnabled,
            TriggerVirtualKey = TriggerVirtualKey,
            TriggerCtrl = TriggerCtrl,
            TriggerAlt = TriggerAlt,
            TriggerShift = TriggerShift,
            TriggerWin = TriggerWin,
            ActionType = ActionType,
            TargetVirtualKey = TargetVirtualKey,
            TargetCtrl = TargetCtrl,
            TargetAlt = TargetAlt,
            TargetShift = TargetShift,
            TargetWin = TargetWin,
            TargetExtraKeys = new List<int>(TargetExtraKeys),
            EnableAppFilter = EnableAppFilter,
            AppFilterIsWhitelist = AppFilterIsWhitelist,
            FilterProcesses = new List<string>(FilterProcesses)
        };
    }
}
