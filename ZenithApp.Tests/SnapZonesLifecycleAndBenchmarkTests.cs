using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Settings;
using ZenithApp.Modules.SnapZones;

namespace ZenithApp.Tests;

public class SnapZonesLifecycleAndBenchmarkTests
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
    public async Task SnapZones_Lifecycle_ZeroOverheadInIdle()
    {
        var logger = new TestLogger();
        var hotkeyService = new MockHotkeyService();
        var settingsService = new TestSettingsService();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long memStart = GC.GetTotalMemory(true);

        using var module = new SnapZonesModule(logger, hotkeyService, settingsService);
        Assert.False(module.IsEnabled);

        // 1. Enable module
        await module.EnableAsync();
        Assert.True(module.IsEnabled);
        Assert.True(hotkeyService.Registered.Count > 0);

        long memEnabledIdle = GC.GetTotalMemory(true);
        long idleDelta = memEnabledIdle - memStart;
        Assert.True(idleDelta < 500 * 1024, $"SnapZones idle memory too high: {idleDelta} bytes");

        // 2. Disable module
        await module.DisableAsync();
        Assert.False(module.IsEnabled);
        Assert.Empty(hotkeyService.Registered);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long memFinal = GC.GetTotalMemory(true);
        long leak = memFinal - memStart;
        Assert.True(leak < 1024 * 1024, $"Retained memory after lifecycle: {leak} bytes");
    }

    [Fact]
    public void SnapHotkeyController_RegistersAllKeys()
    {
        var logger = new TestLogger();
        var hotkeyService = new MockHotkeyService();
        var settings = new SnapZonesSettings();

        using var controller = new SnapHotkeyController(
            hotkeyService, 
            logger, 
            settings, 
            () => ZoneLayoutPreset.TwoColumns5050, 
            () => null);

        controller.RegisterHotkeys();

        Assert.True(hotkeyService.Registered.ContainsKey("Snap_PrevZone"));
        Assert.True(hotkeyService.Registered.ContainsKey("Snap_NextZone"));
        Assert.True(hotkeyService.Registered.ContainsKey("Snap_Zone_1"));
        Assert.True(hotkeyService.Registered.ContainsKey("Snap_Zone_2"));
        Assert.True(hotkeyService.Registered.ContainsKey("Snap_Zone_9"));

        controller.UnregisterHotkeys();
        Assert.Empty(hotkeyService.Registered);
    }
}
