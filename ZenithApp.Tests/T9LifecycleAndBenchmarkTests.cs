using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Input;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Settings;
using ZenithApp.Modules.T9Predictor;

namespace ZenithApp.Tests;

public class T9LifecycleAndBenchmarkTests
{
    private sealed class MockHotkeyService : IHotkeyService
    {
        public Dictionary<string, (KeyModifiers Modifiers, System.Windows.Input.Key Key, Action Action)> Registered { get; } = new();

        public bool RegisterHotkey(string id, KeyModifiers modifiers, System.Windows.Input.Key key, Action action)
        {
            Registered[id] = (modifiers, key, action);
            return true;
        }

        public bool UnregisterHotkey(string id)
        {
            return Registered.Remove(id);
        }

        public void UnregisterAll()
        {
            Registered.Clear();
        }

        public void Dispose()
        {
            UnregisterAll();
        }
    }

    [Fact]
    public async Task T9Predictor_Lifecycle_ZeroOverheadInIdle()
    {
        var logger = new TestLogger();
        using var hookService = new KeyboardHookService();
        var hotkeyService = new MockHotkeyService();
        var settingsService = new TestSettingsService();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long memStart = GC.GetTotalMemory(true);

        using var module = new T9PredictorModule(logger, hookService, hotkeyService, settingsService);
        Assert.False(module.IsEnabled);
        Assert.False(hookService.IsHookActive);

        // 1. Enable module
        await module.EnableAsync();
        Assert.True(module.IsEnabled);
        Assert.True(hookService.IsHookActive);
        Assert.Single(hotkeyService.Registered);

        long memEnabledIdle = GC.GetTotalMemory(true);
        long idleDelta = memEnabledIdle - memStart;
        Assert.True(idleDelta < 500 * 1024, $"T9 idle memory too high: {idleDelta} bytes");

        // 2. Disable module
        await module.DisableAsync();
        Assert.False(module.IsEnabled);
        Assert.False(hookService.IsHookActive);
        Assert.Empty(hotkeyService.Registered);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long memFinal = GC.GetTotalMemory(true);
        long leak = memFinal - memStart;
        Assert.True(leak < 1024 * 1024, $"Retained memory after lifecycle: {leak} bytes");
    }
}
