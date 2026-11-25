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

public class TestKeyboardHookService : IKeyboardHookService
{
    public List<Func<KeyboardHookEventArgs, bool>> Handlers { get; } = new();
    public bool IsHookActive => Handlers.Count > 0;

    public IDisposable Subscribe(Func<KeyboardHookEventArgs, bool> handler)
    {
        Handlers.Add(handler);
        return new DummyDisposable(() => Handlers.Remove(handler));
    }

    public bool Raise(KeyboardHookEventArgs args)
    {
        bool suppressed = false;
        foreach (var h in Handlers.ToList())
        {
            if (h(args)) suppressed = true;
        }
        return suppressed;
    }

    public void Dispose() => Handlers.Clear();

    private sealed class DummyDisposable : IDisposable
    {
        private readonly Action _onDispose;
        public DummyDisposable(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
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

    [Fact]
    public void KeyRemap_SwitchLanguage_RuleConfiguration_And_Validation()
    {
        var rule = new KeyRemapRule
        {
            TriggerVirtualKey = VirtualKeyHelper.VK_CAPITAL,
            ActionType = KeyRemapActionType.SwitchLanguage
        };

        var vm = new EditRuleDialogViewModel(rule);
        Assert.Equal(3, vm.ActionTypeIndex);
        Assert.False(vm.IsTargetVisible);
        Assert.False(vm.IsComboModifiersVisible);
        Assert.Equal("Смена языка ввода (Alt + Shift)", vm.TargetDisplay);
        Assert.True(vm.Validate());

        var result = vm.BuildResult();
        Assert.Equal(KeyRemapActionType.SwitchLanguage, result.ActionType);
        Assert.Equal(VirtualKeyHelper.VK_CAPITAL, result.TriggerVirtualKey);

        var logger = new TestLogger();
        using var hookService = new KeyboardHookService();
        var settingsService = new TestSettingsService();
        using var module = new KeyRemapModule(logger, hookService, settingsService);
        var settingsVm = new KeyRemapSettingsViewModel(module, settingsService);
        var itemVm = new RuleItemViewModel(result, settingsVm);

        Assert.Equal("Сменить язык ввода (Alt + Shift)", itemVm.ActionSummary);
    }

    [Fact]
    public void KeyRemap_ModifierOnlyTrigger_AltShift_ValidatedAndConfigured()
    {
        var rule = new KeyRemapRule();
        var vm = new EditRuleDialogViewModel(rule);

        // Simulate user entering Alt + Shift as trigger combination (vk = 0, alt = true, shift = true)
        vm.SetTrigger(0, ctrl: false, alt: true, shift: true, win: false);

        Assert.Equal("Alt + Shift", vm.TriggerDisplay);

        // Target: Remap to F12 (0x7B)
        vm.SetTarget(0x7B, false, false, false, false);
        Assert.True(vm.Validate());

        var result = vm.BuildResult();
        Assert.Equal(0, result.TriggerVirtualKey);
        Assert.True(result.TriggerAlt);
        Assert.True(result.TriggerShift);
        Assert.False(result.TriggerCtrl);
        Assert.Equal(0x7B, result.TargetVirtualKey);
    }

    [Fact]
    public async Task KeyRemap_LanguageSwitchAction_Execution_SuppressesKey_And_TriggersSwitch()
    {
        var logger = new TestLogger();
        var hookService = new TestKeyboardHookService();
        var settingsService = new TestSettingsService();

        // Rule: CapsLock -> Switch language (Alt + Shift)
        settingsService.Current.KeyRemap.Rules.Add(new KeyRemapRule
        {
            TriggerVirtualKey = VirtualKeyHelper.VK_CAPITAL,
            ActionType = KeyRemapActionType.SwitchLanguage,
            IsEnabled = true
        });

        int switchCallCount = 0;
        KeyboardInputSender.CustomLanguageSwitchAction = () => switchCallCount++;

        try
        {
            using var module = new KeyRemapModule(logger, hookService, settingsService);
            await module.EnableAsync();

            // 1. Initial KeyDown for CapsLock
            var downArgs = new KeyboardHookEventArgs(VirtualKeyHelper.VK_CAPITAL, 0x3A, true, false, false, false, 0);
            var handled = hookService.Raise(downArgs);

            Assert.True(handled);
            Assert.True(downArgs.SuppressKeyPress);
            Assert.Equal(1, switchCallCount);

            // 2. Key repeat while held down -> should be suppressed without re-triggering switch
            var repeatArgs = new KeyboardHookEventArgs(VirtualKeyHelper.VK_CAPITAL, 0x3A, true, false, false, false, 0);
            var repeatHandled = hookService.Raise(repeatArgs);

            Assert.True(repeatHandled);
            Assert.True(repeatArgs.SuppressKeyPress);
            Assert.Equal(1, switchCallCount); // Still 1!

            // 3. KeyUp for CapsLock -> suppressed
            var upArgs = new KeyboardHookEventArgs(VirtualKeyHelper.VK_CAPITAL, 0x3A, false, true, false, false, 0);
            var upHandled = hookService.Raise(upArgs);

            Assert.True(upHandled);
            Assert.True(upArgs.SuppressKeyPress);

            // 4. Second press down -> triggers switch again
            var downArgs2 = new KeyboardHookEventArgs(VirtualKeyHelper.VK_CAPITAL, 0x3A, true, false, false, false, 0);
            var handled2 = hookService.Raise(downArgs2);

            Assert.True(handled2);
            Assert.True(downArgs2.SuppressKeyPress);
            Assert.Equal(2, switchCallCount);

            await module.DisableAsync();
        }
        finally
        {
            KeyboardInputSender.CustomLanguageSwitchAction = null;
        }
    }

    [Fact]
    public async Task KeyRemap_TargetAltShift_RoutesToLanguageSwitch()
    {
        var logger = new TestLogger();
        var hookService = new TestKeyboardHookService();
        var settingsService = new TestSettingsService();

        // Rule: F1 (0x70) -> RemapKeyCombination (TargetAlt = true, TargetShift = true, TargetVk = 0)
        settingsService.Current.KeyRemap.Rules.Add(new KeyRemapRule
        {
            TriggerVirtualKey = 0x70, // F1
            ActionType = KeyRemapActionType.RemapKeyCombination,
            TargetVirtualKey = 0,
            TargetAlt = true,
            TargetShift = true,
            IsEnabled = true
        });

        int switchCallCount = 0;
        KeyboardInputSender.CustomLanguageSwitchAction = () => switchCallCount++;

        try
        {
            using var module = new KeyRemapModule(logger, hookService, settingsService);
            await module.EnableAsync();

            var downArgs = new KeyboardHookEventArgs(0x70, 0x3B, true, false, false, false, 0);
            var handled = hookService.Raise(downArgs);

            Assert.True(handled);
            Assert.True(downArgs.SuppressKeyPress);
            Assert.Equal(1, switchCallCount);

            await module.DisableAsync();
        }
        finally
        {
            KeyboardInputSender.CustomLanguageSwitchAction = null;
        }
    }

    [Fact]
    public async Task KeyRemap_ModifierOnlyTrigger_AltShift_MatchesAndSuppresses()
    {
        var logger = new TestLogger();
        var hookService = new TestKeyboardHookService();
        var settingsService = new TestSettingsService();

        // Rule: Trigger is Alt + Shift (TriggerVirtualKey = 0, TriggerAlt = true, TriggerShift = true)
        // Action: DisableKey
        settingsService.Current.KeyRemap.Rules.Add(new KeyRemapRule
        {
            TriggerVirtualKey = 0,
            TriggerAlt = true,
            TriggerShift = true,
            ActionType = KeyRemapActionType.DisableKey,
            IsEnabled = true
        });

        // Mock physical modifier checker: Alt is held down
        KeyRemapModule.PhysicalModifierChecker = vk => vk == VirtualKeyHelper.VK_MENU || vk == VirtualKeyHelper.VK_LMENU;

        try
        {
            using var module = new KeyRemapModule(logger, hookService, settingsService);
            await module.EnableAsync();

            // When Shift is pressed down while Alt is held:
            var shiftDownArgs = new KeyboardHookEventArgs(VirtualKeyHelper.VK_SHIFT, 0x2A, true, false, false, false, 0);
            var handled = hookService.Raise(shiftDownArgs);

            Assert.True(handled);
            Assert.True(shiftDownArgs.SuppressKeyPress);

            // KeyUp for Shift
            var shiftUpArgs = new KeyboardHookEventArgs(VirtualKeyHelper.VK_SHIFT, 0x2A, false, true, false, false, 0);
            var upHandled = hookService.Raise(shiftUpArgs);

            Assert.True(upHandled);
            Assert.True(shiftUpArgs.SuppressKeyPress);

            await module.DisableAsync();
        }
        finally
        {
            KeyRemapModule.PhysicalModifierChecker = null;
        }
    }

    [Fact]
    public void KeyboardLayoutHelper_FormatsEnglishAndRussianLayouts()
    {
        var enHkl = new nint(0x04090409);
        var enInfo = KeyboardLayoutHelper.GetLayoutInfo(enHkl);
        Assert.Equal(0x0409, enInfo.LanguageId);
        Assert.Equal("EN", enInfo.ShortCode);
        Assert.Equal("English", enInfo.DisplayName);

        var ruHkl = new nint(0x04190419);
        var ruInfo = KeyboardLayoutHelper.GetLayoutInfo(ruHkl);
        Assert.Equal(0x0419, ruInfo.LanguageId);
        Assert.Equal("RU", ruInfo.ShortCode);
        Assert.Equal("Русский", ruInfo.DisplayName);

        var fallbackInfo = KeyboardLayoutHelper.GetLayoutInfo(nint.Zero);
        Assert.Equal("?", fallbackInfo.ShortCode);
        Assert.False(string.IsNullOrWhiteSpace(fallbackInfo.DisplayName));
    }

    [Fact]
    public async Task KeyRemap_LanguageSwitch_TriggersOsdWithNewLayout_WhenEnabled()
    {
        var logger = new TestLogger();
        var hookService = new TestKeyboardHookService();
        var settingsService = new TestSettingsService();

        settingsService.Current.KeyRemap.Rules.Add(new KeyRemapRule
        {
            TriggerVirtualKey = VirtualKeyHelper.VK_CAPITAL,
            ActionType = KeyRemapActionType.SwitchLanguage,
            IsEnabled = true
        });

        nint currentLayout = new nint(0x04090409); // EN
        KeyboardLayoutInfo? notifiedInfo = null;
        bool? notifiedChanged = null;
        using var osdSignal = new ManualResetEventSlim(false);

        KeyboardInputSender.CustomLanguageSwitchAction = () => currentLayout = new nint(0x04190419); // Switch to RU
        KeyRemapModule.ForegroundLayoutHandleReader = () => currentLayout;

        try
        {
            using var module = new KeyRemapModule(logger, hookService, settingsService)
            {
                CustomLanguageOsdHandler = (info, changed) =>
                {
                    notifiedInfo = info;
                    notifiedChanged = changed;
                    osdSignal.Set();
                }
            };
            await module.EnableAsync();

            var downArgs = new KeyboardHookEventArgs(VirtualKeyHelper.VK_CAPITAL, 0x3A, true, false, false, false, 0);
            Assert.True(hookService.Raise(downArgs));

            Assert.True(osdSignal.Wait(TimeSpan.FromSeconds(2)), "Language switch OSD handler was not triggered in time.");
            Assert.NotNull(notifiedInfo);
            Assert.Equal("RU", notifiedInfo.Value.ShortCode);
            Assert.Equal("Русский", notifiedInfo.Value.DisplayName);
            Assert.True(notifiedChanged);

            await module.DisableAsync();
        }
        finally
        {
            KeyboardInputSender.CustomLanguageSwitchAction = null;
            KeyRemapModule.ForegroundLayoutHandleReader = null;
        }
    }

    [Fact]
    public async Task KeyRemap_LanguageSwitch_DoesNotTriggerOsd_WhenSettingDisabled()
    {
        var logger = new TestLogger();
        var hookService = new TestKeyboardHookService();
        var settingsService = new TestSettingsService();
        settingsService.Current.KeyRemap.ShowLanguageSwitchOsd = false;

        settingsService.Current.KeyRemap.Rules.Add(new KeyRemapRule
        {
            TriggerVirtualKey = 0x70, // F1
            ActionType = KeyRemapActionType.RemapKeyCombination,
            TargetVirtualKey = 0,
            TargetAlt = true,
            TargetShift = true,
            IsEnabled = true
        });

        int osdCallCount = 0;
        KeyboardInputSender.CustomLanguageSwitchAction = () => { };
        KeyRemapModule.ForegroundLayoutHandleReader = () => new nint(0x04090409);

        try
        {
            using var module = new KeyRemapModule(logger, hookService, settingsService)
            {
                CustomLanguageOsdHandler = (_, _) => Interlocked.Increment(ref osdCallCount)
            };
            await module.EnableAsync();

            var downArgs = new KeyboardHookEventArgs(0x70, 0x3B, true, false, false, false, 0);
            Assert.True(hookService.Raise(downArgs));

            await Task.Delay(120);
            Assert.Equal(0, osdCallCount);

            await module.DisableAsync();
        }
        finally
        {
            KeyboardInputSender.CustomLanguageSwitchAction = null;
            KeyRemapModule.ForegroundLayoutHandleReader = null;
        }
    }

    [Fact]
    public void KeyRemapSettingsViewModel_ShowLanguageSwitchOsd_DefaultsTrueAndSaves()
    {
        var logger = new TestLogger();
        using var hookService = new KeyboardHookService();
        var settingsService = new TestSettingsService();
        using var module = new KeyRemapModule(logger, hookService, settingsService);

        var vm = new KeyRemapSettingsViewModel(module, settingsService);
        Assert.True(vm.ShowLanguageSwitchOsd);

        vm.ShowLanguageSwitchOsd = false;
        Assert.False(settingsService.Current.KeyRemap.ShowLanguageSwitchOsd);
        Assert.True(settingsService.Saved);
    }
}
