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

    private readonly IKeyboardHookService _hookService;
    private readonly ISettingsService _settingsService;
    private IDisposable? _hookSubscription;

    // Track active synthesized keys to release them accurately on physical release or abort
    private readonly ConcurrentDictionary<int, List<int>> _activeSynthesizedMap = new();

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

        // Query synchronous current state of modifier keys from Windows
        var ctrlPressed = (GetKeyState(VirtualKeyHelper.VK_CONTROL) & 0x8000) != 0;
        var altPressed = (GetKeyState(VirtualKeyHelper.VK_MENU) & 0x8000) != 0;
        var shiftPressed = (GetKeyState(VirtualKeyHelper.VK_SHIFT) & 0x8000) != 0;
        var winPressed = (GetKeyState(VirtualKeyHelper.VK_LWIN) & 0x8000) != 0 || (GetKeyState(VirtualKeyHelper.VK_RWIN) & 0x8000) != 0;

        // Handle KeyUp for an already mapped key
        if (e.IsKeyUp)
        {
            if (_activeSynthesizedMap.TryRemove(vk, out var activeKeys))
            {
                // Release in reverse order
                for (int i = activeKeys.Count - 1; i >= 0; i--)
                {
                    KeyboardInputSender.SendKeyUp(activeKeys[i]);
                }
                e.SuppressKeyPress = true;
                return true;
            }
        }

        // Handle KeyDown: find matching enabled rule
        if (e.IsKeyDown)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (!rule.IsEnabled) continue;

                if (rule.TriggerVirtualKey == vk)
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
                                e.SuppressKeyPress = true;
                                return true;

                            case KeyRemapActionType.RemapKey:
                                if (rule.TargetVirtualKey != 0)
                                {
                                    var activeList = new List<int> { rule.TargetVirtualKey };
                                    _activeSynthesizedMap[vk] = activeList;
                                    KeyboardInputSender.SendKeyDown(rule.TargetVirtualKey);
                                }
                                e.SuppressKeyPress = true;
                                return true;

                            case KeyRemapActionType.RemapKeyCombination:
                                var sequence = new List<int>();
                                if (rule.TargetCtrl && !ctrlPressed) sequence.Add(VirtualKeyHelper.VK_CONTROL);
                                if (rule.TargetAlt && !altPressed) sequence.Add(VirtualKeyHelper.VK_MENU);
                                if (rule.TargetShift && !shiftPressed) sequence.Add(VirtualKeyHelper.VK_SHIFT);
                                if (rule.TargetWin && !winPressed) sequence.Add(VirtualKeyHelper.VK_LWIN);
                                if (rule.TargetVirtualKey != 0) sequence.Add(rule.TargetVirtualKey);

                                _activeSynthesizedMap[vk] = sequence;

                                for (int s = 0; s < sequence.Count; s++)
                                {
                                    KeyboardInputSender.SendKeyDown(sequence[s]);
                                }

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

        var ctrlDown = (GetKeyState(VirtualKeyHelper.VK_CONTROL) & 0x8000) != 0;
        var shiftDown = (GetKeyState(VirtualKeyHelper.VK_SHIFT) & 0x8000) != 0;
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
        else if (e.IsKeyUp && (e.VirtualCode == VirtualKeyHelper.VK_CONTROL || e.VirtualCode == VirtualKeyHelper.VK_SHIFT || e.VirtualCode == VirtualKeyHelper.VK_ESCAPE))
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
            for (int i = pair.Value.Count - 1; i >= 0; i--)
            {
                KeyboardInputSender.SendKeyUp(pair.Value[i]);
            }
        }
        _activeSynthesizedMap.Clear();
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
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}
