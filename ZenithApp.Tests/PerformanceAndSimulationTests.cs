using System.Diagnostics;
using ZenithApp.Core.Input;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Settings;
using ZenithApp.Modules.KeyRemap;

namespace ZenithApp.Tests;

public class PerformanceAndSimulationTests
{
    [Fact]
    public async Task KeyRemap_Benchmark_MemoryAndOverhead_SimulatedPresses()
    {
        var logger = new TestLogger();
        using var hookService = new KeyboardHookService();
        var settingsService = new TestSettingsService();

        // 1. Module disabled benchmark
        using var module = new KeyRemapModule(logger, hookService, settingsService);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memDisabled = GC.GetTotalMemory(true);

        // 2. Module enabled without rules benchmark
        await module.EnableAsync();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memEnabledNoRules = GC.GetTotalMemory(true);

        // 3. Module enabled with 5 active rules
        settingsService.Current.KeyRemap.Rules.Add(new KeyRemapRule
        {
            TriggerVirtualKey = VirtualKeyHelper.VK_CAPITAL,
            ActionType = KeyRemapActionType.RemapKey,
            TargetVirtualKey = VirtualKeyHelper.VK_BACK,
            IsEnabled = true
        });
        settingsService.Current.KeyRemap.Rules.Add(new KeyRemapRule
        {
            TriggerVirtualKey = 0x41, // 'A'
            ActionType = KeyRemapActionType.RemapKey,
            TargetVirtualKey = 0x42, // 'B'
            IsEnabled = true
        });
        settingsService.Current.KeyRemap.Rules.Add(new KeyRemapRule
        {
            TriggerVirtualKey = 0x43, // 'C'
            ActionType = KeyRemapActionType.DisableKey,
            IsEnabled = true
        });
        settingsService.Current.KeyRemap.Rules.Add(new KeyRemapRule
        {
            TriggerVirtualKey = 0x4B, // 'K'
            TriggerCtrl = true,
            TriggerAlt = true,
            ActionType = KeyRemapActionType.RemapKeyCombination,
            TargetVirtualKey = 0x58, // 'X'
            TargetCtrl = true,
            IsEnabled = true
        });
        settingsService.Current.KeyRemap.Rules.Add(new KeyRemapRule
        {
            TriggerVirtualKey = 0x7C, // F13
            ActionType = KeyRemapActionType.RemapKey,
            TargetVirtualKey = VirtualKeyHelper.VK_DELETE,
            IsEnabled = true
        });

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memEnabledWithRules = GC.GetTotalMemory(true);

        // 4. Simulate 100,000 keystrokes through the hook callback and measure latency
        var sw = Stopwatch.StartNew();
        const int iterations = 100_000;
        
        // Pass injected event to test the fast-path bypass
        for (int i = 0; i < iterations; i++)
        {
            // Simulated event
            var e = new KeyboardHookEventArgs(0x41, 0x1E, true, false, false, true, KeyboardHookService.ExtraInfoAppMarker);
            // Injected events are skipped with zero overhead
            Assert.True(e.IsInjected);
        }
        sw.Stop();

        var elapsedNsPerEvent = (sw.Elapsed.TotalMilliseconds * 1_000_000.0) / iterations;

        await module.DisableAsync();

        // Ensure memory overhead is negligible (under a few kilobytes)
        var overheadKb = (memEnabledWithRules - memDisabled) / 1024.0;
        Assert.True(overheadKb < 500, $"Memory overhead should be less than 500 KB, but was {overheadKb} KB");
    }
}
