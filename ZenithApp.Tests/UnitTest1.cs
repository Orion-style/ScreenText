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
}
