using System.Diagnostics;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Settings;
using ZenithApp.Modules.Dock;

namespace ZenithApp.Tests;

public class DockPerformanceAndSimulationTests
{
    [Fact]
    public async Task Dock_Benchmark_MemoryAndProcessLifecycle()
    {
        var logger = new TestLogger();
        var settingsService = new TestSettingsService();
        var hotkeyService = new TestHotkeyService();

        // 1. Module disabled benchmark
        using var module = new DockModule(logger, settingsService, hotkeyService);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memDisabled = GC.GetTotalMemory(true);

        // 2. Module enabled with 10 pinned items
        for (int i = 0; i < 10; i++)
        {
            module.Settings.Items.Add(new DockItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = $"App {i}",
                TargetPath = @"C:\Windows\explorer.exe"
            });
        }

        await module.EnableAsync();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memEnabledWith10Items = GC.GetTotalMemory(true);

        // Verify memory overhead is minimal (under 500 KB)
        var overheadKb = (memEnabledWith10Items - memDisabled) / 1024.0;
        Assert.True(overheadKb < 500, $"Memory overhead should be < 500 KB, but was {overheadKb} KB");

        // 3. Simulate 5 launch and check cycles
        for (int cycle = 0; cycle < 5; cycle++)
        {
            var isRunning = ProcessInteractionService.IsRunning("explorer.exe", out var proc);
            Assert.True(isRunning);
            Assert.NotNull(proc);

            // Fast status check without lingering allocations
            var checkSw = Stopwatch.StartNew();
            for (int k = 0; k < 10; k++)
            {
                ProcessInteractionService.IsRunning("notepad.exe", out _);
            }
            checkSw.Stop();
            Assert.True(checkSw.ElapsedMilliseconds < 50, "Status checks should be ultra-fast");
        }

        // 4. Cleanup
        await module.DisableAsync();
        Assert.False(module.IsEnabled);
    }
}
