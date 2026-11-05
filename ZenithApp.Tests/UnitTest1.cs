using ZenithApp.Core.Input;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Settings;
using ZenithApp.Modules.KeyRemap;

namespace ZenithApp.Tests;

public class TestLogger : IAppLogger
{
    public List<string> Logs { get; } = new();
    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        Logs.Add($"[{level}] {message} {exception?.Message}");
    }
}

public class TestSettingsService : ISettingsService
{
    public AppSettings Current { get; } = new();
    public string SettingsFilePath => "test_settings.json";
    public string SettingsDirectory => "test_dir";
    public bool Saved { get; private set; }
    public event Action<AppSettings>? SettingsChanged;

    public void Load() {}
    public void Save()
    {
        Saved = true;
        SettingsChanged?.Invoke(Current);
    }
}

public class KeyRemapModuleTests
{
    [Fact]
    public void HookService_ReferenceCounting_ActivatesAndDeactivatesHook()
    {
        using var hookService = new KeyboardHookService();
        Assert.False(hookService.IsHookActive);

        var sub1 = hookService.Subscribe(_ => false);
        Assert.True(hookService.IsHookActive);

        var sub2 = hookService.Subscribe(_ => false);
        Assert.True(hookService.IsHookActive);

        sub1.Dispose();
        Assert.True(hookService.IsHookActive); // Still one subscriber left

        sub2.Dispose();
        Assert.False(hookService.IsHookActive); // Unhooked when 0 subscribers
    }

    [Fact]
    public async Task KeyRemapModule_ZeroOverhead_WhenDisabled()
    {
        var logger = new TestLogger();
        using var hookService = new KeyboardHookService();
        var settingsService = new TestSettingsService();

        using var module = new KeyRemapModule(logger, hookService, settingsService);

        Assert.False(module.IsEnabled);
        Assert.False(hookService.IsHookActive);

        await module.EnableAsync();
        Assert.True(module.IsEnabled);
        Assert.True(hookService.IsHookActive);

        await module.DisableAsync();
        Assert.False(module.IsEnabled);
        Assert.False(hookService.IsHookActive);
    }

    [Fact]
    public async Task KeyRemap_RuleStructure_ConfiguredCorrectly()
    {
        var logger = new TestLogger();
        using var hookService = new KeyboardHookService();
        var settingsService = new TestSettingsService();

        // Rule: disable CapsLock (VK_CAPITAL = 0x14)
        settingsService.Current.KeyRemap.Rules.Add(new KeyRemapRule
        {
            TriggerVirtualKey = VirtualKeyHelper.VK_CAPITAL,
            ActionType = KeyRemapActionType.DisableKey,
            IsEnabled = true
        });

        using var module = new KeyRemapModule(logger, hookService, settingsService);
        await module.EnableAsync();

        Assert.Single(settingsService.Current.KeyRemap.Rules);
        Assert.True(settingsService.Current.KeyRemap.Rules[0].IsEnabled);
        Assert.Equal(KeyRemapActionType.DisableKey, settingsService.Current.KeyRemap.Rules[0].ActionType);

        await module.DisableAsync();
    }

    [Fact]
    public void KeyRemap_SecurityValidation_PreventsDisablingCtrlAltDel()
    {
        var rule = new KeyRemapRule
        {
            TriggerVirtualKey = VirtualKeyHelper.VK_DELETE,
            TriggerCtrl = true,
            TriggerAlt = true,
            ActionType = KeyRemapActionType.DisableKey
        };

        var dialogVm = new EditRuleDialogViewModel(rule);
        var isValid = dialogVm.Validate();

        Assert.False(isValid);
        Assert.True(dialogVm.HasValidationError);
        Assert.Contains("Ctrl+Alt+Delete", dialogVm.ValidationError);
    }

    [Fact]
    public void KeyRemap_SecurityValidation_PreventsDisablingEmergencyShortcut()
    {
        var rule = new KeyRemapRule
        {
            TriggerVirtualKey = VirtualKeyHelper.VK_ESCAPE,
            TriggerCtrl = true,
            TriggerShift = true,
            ActionType = KeyRemapActionType.DisableKey
        };

        var dialogVm = new EditRuleDialogViewModel(rule);
        var isValid = dialogVm.Validate();

        Assert.False(isValid);
        Assert.True(dialogVm.HasValidationError);
        Assert.Contains("Ctrl+Shift+Esc", dialogVm.ValidationError);
    }

    [Fact]
    public void KeyRemap_ConflictDetection_DetectsDuplicateTriggers()
    {
        var logger = new TestLogger();
        using var hookService = new KeyboardHookService();
        var settingsService = new TestSettingsService();

        settingsService.Current.KeyRemap.Rules.Add(new KeyRemapRule
        {
            TriggerVirtualKey = VirtualKeyHelper.VK_CAPITAL,
            ActionType = KeyRemapActionType.DisableKey,
            IsEnabled = true
        });

        settingsService.Current.KeyRemap.Rules.Add(new KeyRemapRule
        {
            TriggerVirtualKey = VirtualKeyHelper.VK_CAPITAL,
            ActionType = KeyRemapActionType.RemapKey,
            TargetVirtualKey = VirtualKeyHelper.VK_BACK,
            IsEnabled = true
        });

        using var module = new KeyRemapModule(logger, hookService, settingsService);
        var vm = new KeyRemapSettingsViewModel(module, settingsService);

        Assert.Equal(2, vm.Rules.Count);
        Assert.True(vm.Rules[0].HasConflict);
        Assert.True(vm.Rules[1].HasConflict);
    }

    [Fact]
    public void KeyRemap_EmergencyEscape_DisablesAllRules()
    {
        var logger = new TestLogger();
        using var hookService = new KeyboardHookService();
        var settingsService = new TestSettingsService();

        var rule = new KeyRemapRule
        {
            TriggerVirtualKey = VirtualKeyHelper.VK_CAPITAL,
            ActionType = KeyRemapActionType.RemapKey,
            TargetVirtualKey = VirtualKeyHelper.VK_BACK,
            IsEnabled = true
        };
        settingsService.Current.KeyRemap.Rules.Add(rule);

        using var module = new KeyRemapModule(logger, hookService, settingsService);
        Assert.True(rule.IsEnabled);

        module.TriggerEmergencyShutdown();

        Assert.False(rule.IsEnabled);
        Assert.True(settingsService.Saved);
    }

    [Fact]
    public void KeyRemap_EditRuleDialogViewModel_SupportsCombinationTriggerAndModifiers()
    {
        var rule = new KeyRemapRule();
        var vm = new EditRuleDialogViewModel(rule);

        // Initially no key is set
        Assert.False(vm.Validate());

        // Set trigger combination via SetTrigger: Ctrl + Alt + K (0x4B)
        vm.SetTrigger(0x4B, ctrl: true, alt: true, shift: false, win: false);

        Assert.True(vm.TriggerCtrl);
        Assert.True(vm.TriggerAlt);
        Assert.False(vm.TriggerShift);
        Assert.False(vm.TriggerWin);
        Assert.Contains("Ctrl", vm.TriggerDisplay);
        Assert.Contains("Alt", vm.TriggerDisplay);
        Assert.Contains("K", vm.TriggerDisplay);

        // Toggle Win modifier checkbox via property
        vm.TriggerWin = true;
        Assert.True(vm.TriggerWin);
        Assert.Contains("Win", vm.TriggerDisplay);

        // Set target combination via SetTarget: Shift + X (0x58)
        vm.SetTarget(0x58, ctrl: false, alt: false, shift: true, win: false);

        // Should auto-switch ActionTypeIndex to 1 (RemapKeyCombination)
        Assert.Equal(1, vm.ActionTypeIndex);
        Assert.True(vm.TargetShift);
        Assert.False(vm.TargetCtrl);
        Assert.Contains("Shift", vm.TargetDisplay);
        Assert.Contains("X", vm.TargetDisplay);

        // Build result and verify rule has combination trigger and target
        var result = vm.BuildResult();
        Assert.Equal(0x4B, result.TriggerVirtualKey);
        Assert.True(result.TriggerCtrl);
        Assert.True(result.TriggerAlt);
        Assert.False(result.TriggerShift);
        Assert.True(result.TriggerWin);
        Assert.Equal(KeyRemapActionType.RemapKeyCombination, result.ActionType);
        Assert.Equal(0x58, result.TargetVirtualKey);
        Assert.True(result.TargetShift);
        Assert.False(result.TargetCtrl);
    }

    [Fact]
    public void KeyRemap_VirtualKeyHelper_AreKeysEquivalent_And_Normalize()
    {
        Assert.True(VirtualKeyHelper.AreKeysEquivalent(VirtualKeyHelper.VK_CONTROL, VirtualKeyHelper.VK_LCONTROL));
        Assert.True(VirtualKeyHelper.AreKeysEquivalent(VirtualKeyHelper.VK_CONTROL, VirtualKeyHelper.VK_RCONTROL));
        Assert.True(VirtualKeyHelper.AreKeysEquivalent(VirtualKeyHelper.VK_MENU, VirtualKeyHelper.VK_LMENU));
        Assert.True(VirtualKeyHelper.AreKeysEquivalent(VirtualKeyHelper.VK_SHIFT, VirtualKeyHelper.VK_RSHIFT));
        Assert.True(VirtualKeyHelper.AreKeysEquivalent(VirtualKeyHelper.VK_LWIN, VirtualKeyHelper.VK_RWIN));
        Assert.False(VirtualKeyHelper.AreKeysEquivalent(VirtualKeyHelper.VK_CONTROL, VirtualKeyHelper.VK_MENU));

        Assert.Equal(VirtualKeyHelper.VK_CONTROL, VirtualKeyHelper.NormalizeVirtualKey(VirtualKeyHelper.VK_LCONTROL));
        Assert.Equal(VirtualKeyHelper.VK_MENU, VirtualKeyHelper.NormalizeVirtualKey(VirtualKeyHelper.VK_LMENU));
        Assert.Equal(VirtualKeyHelper.VK_SHIFT, VirtualKeyHelper.NormalizeVirtualKey(VirtualKeyHelper.VK_RSHIFT));
        Assert.Equal(VirtualKeyHelper.VK_LWIN, VirtualKeyHelper.NormalizeVirtualKey(VirtualKeyHelper.VK_RWIN));
        Assert.Equal(0x41, VirtualKeyHelper.NormalizeVirtualKey(0x41)); // 'A' unchanged
    }

    [Fact]
    public void KeyRemap_CombinationConflictDetection_DifferentiatesModifiers()
    {
        var logger = new TestLogger();
        using var hookService = new KeyboardHookService();
        var settingsService = new TestSettingsService();

        // Rule 1: Ctrl + K
        settingsService.Current.KeyRemap.Rules.Add(new KeyRemapRule
        {
            TriggerVirtualKey = 0x4B,
            TriggerCtrl = true,
            ActionType = KeyRemapActionType.RemapKey,
            TargetVirtualKey = 0x41,
            IsEnabled = true
        });

        // Rule 2: Alt + K (different modifier -> no conflict with Rule 1)
        settingsService.Current.KeyRemap.Rules.Add(new KeyRemapRule
        {
            TriggerVirtualKey = 0x4B,
            TriggerAlt = true,
            ActionType = KeyRemapActionType.RemapKey,
            TargetVirtualKey = 0x42,
            IsEnabled = true
        });

        // Rule 3: Plain K (no modifiers -> no conflict)
        settingsService.Current.KeyRemap.Rules.Add(new KeyRemapRule
        {
            TriggerVirtualKey = 0x4B,
            ActionType = KeyRemapActionType.RemapKey,
            TargetVirtualKey = 0x43,
            IsEnabled = true
        });

        using var module = new KeyRemapModule(logger, hookService, settingsService);
        var vm = new KeyRemapSettingsViewModel(module, settingsService);

        Assert.Equal(3, vm.Rules.Count);
        Assert.False(vm.Rules[0].HasConflict);
        Assert.False(vm.Rules[1].HasConflict);
        Assert.False(vm.Rules[2].HasConflict);

        // Add Rule 4: Duplicate of Ctrl + K -> should trigger conflict on both
        vm.AddRule(new KeyRemapRule
        {
            TriggerVirtualKey = 0x4B,
            TriggerCtrl = true,
            ActionType = KeyRemapActionType.DisableKey,
            IsEnabled = true
        });

        Assert.Equal(4, vm.Rules.Count);
        Assert.True(vm.Rules[0].HasConflict);
        Assert.False(vm.Rules[1].HasConflict);
        Assert.False(vm.Rules[2].HasConflict);
        Assert.True(vm.Rules[3].HasConflict);
    }
}
