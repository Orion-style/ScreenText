using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ZenithApp.Core.Input;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;

namespace ZenithApp.Modules.KeyRemap;

public sealed class KeyRemapModule : BaseFeatureModule
{
    public const string ModuleId = "key_remap";

    private sealed class ActiveRemapState
    {
        public List<int> PressedKeys { get; } = new();
        public List<int> TemporarilyReleasedModifiers { get; } = new();
    }

    private readonly IKeyboardHookService _hookService;
    private readonly ISettingsService _settingsService;
    private IDisposable? _hookSubscription;

    // Track active synthesized keys and temporarily released modifiers
    private readonly ConcurrentDictionary<int, ActiveRemapState> _activeSynthesizedMap = new();

    // Track keys that are currently suppressed/disabled to suppress matching KeyUp events
    private readonly ConcurrentDictionary<int, bool> _activeDisabledKeys = new();

    // Emergency escape tracking: Ctrl+Shift+Esc held
    private long _emergencyStartTimeTicks;
    private bool _emergencyTriggered;

    public event Action<string>? EmergencyEscapeTriggered;
    public event Action? RulesChanged;

    public KeyRemapModule(
        IAppLogger logger,
        IKeyboardHookService hookService,
        ISettingsService settingsService) : base(logger)
    {
        _hookService = hookService;
        _settingsService = settingsService;
    }

    public override string Id => ModuleId;
    public override string Name => "Переназначение клавиш";
    public override string Description => "Переназначение клавиш и сочетаний на уровне системного хука с защитой от зацикливания и аварийным выходом.";

    public KeyRemapSettings Settings => _settingsService.Current.KeyRemap;

    protected override Task OnEnableAsync()
    {
        _hookSubscription = _hookService.Subscribe(OnLowLevelKeyboardEvent);
        Logger.LogInfo("[KeyRemapModule] Активирован. Низкоуровневый хук подключен.");
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        _hookSubscription?.Dispose();
        _hookSubscription = null;
        ReleaseAllActiveSynthesizedKeys();
        Logger.LogInfo("[KeyRemapModule] Отключен. Хук снят, виртуальные нажатия сброшены.");
        return Task.CompletedTask;
    }

    public void NotifyRulesChanged()
    {
        _settingsService.Save();
        RulesChanged?.Invoke();
    }

    private bool OnLowLevelKeyboardEvent(KeyboardHookEventArgs e)
    {
        // Don't process injected keystrokes
        if (e.IsInjected) return false;

        // 1. Check emergency escape combination
        if (CheckEmergencyEscape(e))
        {
            return true; // Suppressed
        }

        // 2. Fetch active rules
        var rules = Settings.Rules;
        if (rules == null || rules.Count == 0) return false;

        var vk = e.VirtualCode;

        // Handle KeyUp for an already mapped or disabled key
        if (e.IsKeyUp)
        {
            if (_activeDisabledKeys.TryRemove(vk, out _))
            {
                e.SuppressKeyPress = true;
                return true;
            }

            if (_activeSynthesizedMap.TryRemove(vk, out var activeState))
            {
                // Release in reverse order (target key, then synthesized modifiers)
                for (int i = activeState.PressedKeys.Count - 1; i >= 0; i--)
                {
                    KeyboardInputSender.SendKeyUp(activeState.PressedKeys[i]);
                }

                // Restore any modifiers that were temporarily released, if the user is still physically holding them
                for (int i = 0; i < activeState.TemporarilyReleasedModifiers.Count; i++)
                {
                    var mod = activeState.TemporarilyReleasedModifiers[i];
                    if (IsModifierPhysicallyPressed(mod))
                    {
                        KeyboardInputSender.SendKeyDown(mod);
                    }
                }

                e.SuppressKeyPress = true;
                return true;
            }
        }

        // Query current physical modifier state (excluding vk itself if vk is a modifier key)
        var ctrlPressed = !VirtualKeyHelper.IsControlKey(vk) && IsModifierPhysicallyPressed(VirtualKeyHelper.VK_CONTROL);
        var altPressed = !VirtualKeyHelper.IsAltKey(vk) && IsModifierPhysicallyPressed(VirtualKeyHelper.VK_MENU);
        var shiftPressed = !VirtualKeyHelper.IsShiftKey(vk) && IsModifierPhysicallyPressed(VirtualKeyHelper.VK_SHIFT);
        var winPressed = !VirtualKeyHelper.IsWinKey(vk) && (IsModifierPhysicallyPressed(VirtualKeyHelper.VK_LWIN) || IsModifierPhysicallyPressed(VirtualKeyHelper.VK_RWIN));

        // Handle KeyDown: find matching enabled rule
        if (e.IsKeyDown)
        {
            // Handle key repeat for already active synthesized keys
            if (_activeSynthesizedMap.TryGetValue(vk, out var existingState))
            {
                if (existingState.PressedKeys.Count > 0)
                {
                    var targetKey = existingState.PressedKeys[^1];
                    KeyboardInputSender.SendKeyDown(targetKey);
                }
                e.SuppressKeyPress = true;
                return true;
            }

            if (_activeDisabledKeys.ContainsKey(vk))
            {
                e.SuppressKeyPress = true;
                return true;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (!rule.IsEnabled) continue;

                if (VirtualKeyHelper.AreKeysEquivalent(rule.TriggerVirtualKey, vk))
                {
                    // Check modifiers match
                    var matchMods = (rule.TriggerCtrl == ctrlPressed) &&
                                     (rule.TriggerAlt == altPressed) &&
                                     (rule.TriggerShift == shiftPressed) &&
                                     (rule.TriggerWin == winPressed);

                    if (matchMods)
                    {
                        // Check process filter if enabled
                        if (rule.EnableAppFilter && !IsProcessAllowed(rule))
                        {
                            continue;
                        }

                        // Apply action
                        switch (rule.ActionType)
                        {
                            case KeyRemapActionType.DisableKey:
                                _activeDisabledKeys[vk] = true;
                                e.SuppressKeyPress = true;
                                return true;

                            case KeyRemapActionType.RemapKey:
                            case KeyRemapActionType.RemapKeyCombination:
                                var targetCtrl = (rule.ActionType == KeyRemapActionType.RemapKeyCombination) && rule.TargetCtrl;
                                var targetAlt = (rule.ActionType == KeyRemapActionType.RemapKeyCombination) && rule.TargetAlt;
                                var targetShift = (rule.ActionType == KeyRemapActionType.RemapKeyCombination) && rule.TargetShift;
                                var targetWin = (rule.ActionType == KeyRemapActionType.RemapKeyCombination) && rule.TargetWin;
                                var targetKey = rule.TargetVirtualKey;

                                var remapState = new ActiveRemapState();

                                // 1. Temporarily release physical modifiers that must not be active for target
                                if (ctrlPressed && !targetCtrl)
                                {
                                    remapState.TemporarilyReleasedModifiers.Add(VirtualKeyHelper.VK_CONTROL);
                                    KeyboardInputSender.SendKeyUp(VirtualKeyHelper.VK_CONTROL);
                                }
                                if (altPressed && !targetAlt)
                                {
                                    remapState.TemporarilyReleasedModifiers.Add(VirtualKeyHelper.VK_MENU);
                                    KeyboardInputSender.SendKeyUp(VirtualKeyHelper.VK_MENU);
                                }
                                if (shiftPressed && !targetShift)
                                {
                                    remapState.TemporarilyReleasedModifiers.Add(VirtualKeyHelper.VK_SHIFT);
                                    KeyboardInputSender.SendKeyUp(VirtualKeyHelper.VK_SHIFT);
                                }
                                if (winPressed && !targetWin)
                                {
                                    remapState.TemporarilyReleasedModifiers.Add(VirtualKeyHelper.VK_LWIN);
                                    KeyboardInputSender.SendKeyUp(VirtualKeyHelper.VK_LWIN);
                                }

                                // 2. Synthesize target modifiers that are not currently held
                                if (targetCtrl && !ctrlPressed)
                                {
                                    remapState.PressedKeys.Add(VirtualKeyHelper.VK_CONTROL);
                                    KeyboardInputSender.SendKeyDown(VirtualKeyHelper.VK_CONTROL);
                                }
                                if (targetAlt && !altPressed)
                                {
                                    remapState.PressedKeys.Add(VirtualKeyHelper.VK_MENU);
                                    KeyboardInputSender.SendKeyDown(VirtualKeyHelper.VK_MENU);
                                }
                                if (targetShift && !shiftPressed)
                                {
                                    remapState.PressedKeys.Add(VirtualKeyHelper.VK_SHIFT);
                                    KeyboardInputSender.SendKeyDown(VirtualKeyHelper.VK_SHIFT);
                                }
                                if (targetWin && !winPressed)
                                {
                                    remapState.PressedKeys.Add(VirtualKeyHelper.VK_LWIN);
                                    KeyboardInputSender.SendKeyDown(VirtualKeyHelper.VK_LWIN);
                                }

                                // 3. Synthesize target primary key
                                if (targetKey != 0)
                                {
                                    remapState.PressedKeys.Add(targetKey);
                                    KeyboardInputSender.SendKeyDown(targetKey);
                                }

                                _activeSynthesizedMap[vk] = remapState;
                                e.SuppressKeyPress = true;
                                return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private bool CheckEmergencyEscape(KeyboardHookEventArgs e)
    {
        if (!Settings.EmergencyEscapeEnabled) return false;

        var ctrlDown = IsModifierPhysicallyPressed(VirtualKeyHelper.VK_CONTROL);
        var shiftDown = IsModifierPhysicallyPressed(VirtualKeyHelper.VK_SHIFT);
        var isEsc = e.VirtualCode == VirtualKeyHelper.VK_ESCAPE;

        if (ctrlDown && shiftDown && isEsc)
        {
            if (e.IsKeyDown)
            {
                if (Interlocked.Read(ref _emergencyStartTimeTicks) == 0)
                {
                    Interlocked.Exchange(ref _emergencyStartTimeTicks, Stopwatch.GetTimestamp());
                    _emergencyTriggered = false;
                }
                else
                {
                    var elapsedMs = (Stopwatch.GetTimestamp() - Interlocked.Read(ref _emergencyStartTimeTicks)) * 1000.0 / Stopwatch.Frequency;
                    if (elapsedMs >= Settings.EmergencyHoldDurationMs && !_emergencyTriggered)
                    {
                        _emergencyTriggered = true;
                        TriggerEmergencyShutdown();
                        return true;
                    }
                }
            }
            else if (e.IsKeyUp)
            {
                Interlocked.Exchange(ref _emergencyStartTimeTicks, 0);
                _emergencyTriggered = false;
            }
        }
        else if (e.IsKeyUp && (VirtualKeyHelper.IsControlKey(e.VirtualCode) || VirtualKeyHelper.IsShiftKey(e.VirtualCode) || e.VirtualCode == VirtualKeyHelper.VK_ESCAPE))
        {
            Interlocked.Exchange(ref _emergencyStartTimeTicks, 0);
            _emergencyTriggered = false;
        }

        return false;
    }

    public void TriggerEmergencyShutdown()
    {
        Logger.LogWarning("[KeyRemapModule] Сработал АВАРИЙНЫЙ ВЫХОД! Все правила переназначения немедленно отключены.");

        // Disable all rules
        foreach (var rule in Settings.Rules)
        {
            rule.IsEnabled = false;
        }

        _settingsService.Save();
        ReleaseAllActiveSynthesizedKeys();

        // Fire notification in thread-pool/UI
        Task.Run(() =>
        {
            EmergencyEscapeTriggered?.Invoke("Аварийный выход: все правила переназначения клавиатуры отключены для вашей безопасности.");
            RulesChanged?.Invoke();
        });
    }

    private void ReleaseAllActiveSynthesizedKeys()
    {
        foreach (var pair in _activeSynthesizedMap)
        {
            for (int i = pair.Value.PressedKeys.Count - 1; i >= 0; i--)
            {
                KeyboardInputSender.SendKeyUp(pair.Value.PressedKeys[i]);
            }
        }
        _activeSynthesizedMap.Clear();
        _activeDisabledKeys.Clear();
    }

    private static bool IsModifierPhysicallyPressed(int modVk)
    {
        return modVk switch
        {
            VirtualKeyHelper.VK_CONTROL => ((GetAsyncKeyState(VirtualKeyHelper.VK_CONTROL) & 0x8000) != 0) ||
                                           ((GetKeyState(VirtualKeyHelper.VK_CONTROL) & 0x8000) != 0) ||
                                           ((GetAsyncKeyState(VirtualKeyHelper.VK_LCONTROL) & 0x8000) != 0) ||
                                           ((GetAsyncKeyState(VirtualKeyHelper.VK_RCONTROL) & 0x8000) != 0),
            VirtualKeyHelper.VK_MENU => ((GetAsyncKeyState(VirtualKeyHelper.VK_MENU) & 0x8000) != 0) ||
                                        ((GetKeyState(VirtualKeyHelper.VK_MENU) & 0x8000) != 0) ||
                                        ((GetAsyncKeyState(VirtualKeyHelper.VK_LMENU) & 0x8000) != 0) ||
                                        ((GetAsyncKeyState(VirtualKeyHelper.VK_RMENU) & 0x8000) != 0),
            VirtualKeyHelper.VK_SHIFT => ((GetAsyncKeyState(VirtualKeyHelper.VK_SHIFT) & 0x8000) != 0) ||
                                         ((GetKeyState(VirtualKeyHelper.VK_SHIFT) & 0x8000) != 0) ||
                                         ((GetAsyncKeyState(VirtualKeyHelper.VK_LSHIFT) & 0x8000) != 0) ||
                                         ((GetAsyncKeyState(VirtualKeyHelper.VK_RSHIFT) & 0x8000) != 0),
            VirtualKeyHelper.VK_LWIN or VirtualKeyHelper.VK_RWIN => ((GetAsyncKeyState(VirtualKeyHelper.VK_LWIN) & 0x8000) != 0) ||
                                                                     ((GetKeyState(VirtualKeyHelper.VK_LWIN) & 0x8000) != 0) ||
                                                                     ((GetAsyncKeyState(VirtualKeyHelper.VK_RWIN) & 0x8000) != 0) ||
                                                                     ((GetKeyState(VirtualKeyHelper.VK_RWIN) & 0x8000) != 0),
            _ => ((GetAsyncKeyState(modVk) & 0x8000) != 0) || ((GetKeyState(modVk) & 0x8000) != 0)
        };
    }

    private static bool IsProcessAllowed(KeyRemapRule rule)
    {
        if (rule.FilterProcesses.Count == 0) return true;

        var fgWindow = GetForegroundWindow();
        if (fgWindow == nint.Zero) return true;

        GetWindowThreadProcessId(fgWindow, out uint pid);
        if (pid == 0) return true;

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            var procName = proc.ProcessName;
            var contains = rule.FilterProcesses.Any(p => string.Equals(p, procName, StringComparison.OrdinalIgnoreCase)
                                                      || string.Equals(p, procName + ".exe", StringComparison.OrdinalIgnoreCase));
            return rule.AppFilterIsWhitelist ? contains : !contains;
        }
        catch
        {
            return true;
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}
