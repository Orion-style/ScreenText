using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Threading;
using ZenithApp.Core.Input;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Settings;
using ZenithApp.Modules.QuickLook;

namespace ZenithApp.Tests;

public class QuickLookMemoryAndLifecycleTests
{
    [Fact]
    public async Task QuickLook_Lifecycle_MemoryBenchmark()
    {
        var logger = new TestLogger();
        using var hookService = new KeyboardHookService();
        var settingsService = new TestSettingsService();

        var proc = Process.GetCurrentProcess();

        // 1. Initial State: Module Disabled
        // JIT and framework warmup before baseline
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        proc.Refresh();
        long memStart = GC.GetTotalMemory(true);

        using var module = new QuickLookModule(logger, hookService, settingsService);
        Assert.False(module.IsEnabled);
        Assert.False(hookService.IsHookActive);

        // 2. Module Enabled (Idle)
        await module.EnableAsync();
        Assert.True(module.IsEnabled);
        Assert.True(hookService.IsHookActive);

        proc.Refresh();
        long memEnabledIdle = GC.GetTotalMemory(true);
        long idleDelta = memEnabledIdle - memStart;
        // Idle overhead is negligible (< 500 KB)
        Assert.True(idleDelta < 500 * 1024, $"Idle overhead too high: {idleDelta} bytes");

        // 3. Prepare test files: Text, Image
        var tempFolder = Path.Combine(Path.GetTempPath(), "ZenithQuickLookTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempFolder);

        var txtFile = Path.Combine(tempFolder, "sample.txt");
        await File.WriteAllTextAsync(txtFile, "Sample content line 1\nSample content line 2", Encoding.UTF8);

        var imgFile = Path.Combine(tempFolder, "sample.png");
        using (var bmp = new Bitmap(200, 200))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.CornflowerBlue);
            bmp.Save(imgFile, ImageFormat.Png);
        }

        // 4. Test TextFileReader
        var textResult = await TextFileReader.ReadTextPreviewAsync(txtFile, 1024 * 1024);
        Assert.NotNull(textResult);
        Assert.Contains("Sample content", textResult.Content);

        // 5. Test Content Detection
        Assert.Equal(QuickLookContentType.Text, ContentDetector.DetectContentType(txtFile, module.Settings));
        Assert.Equal(QuickLookContentType.Image, ContentDetector.DetectContentType(imgFile, module.Settings));

        // 6. Disable module & verify complete cleanup
        await module.DisableAsync();
        Assert.False(module.IsEnabled);
        Assert.False(hookService.IsHookActive);

        // Clean up temp files
        Directory.Delete(tempFolder, true);

        // Force GC
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long memFinal = GC.GetTotalMemory(true);
        long leak = memFinal - memStart;
        // Total retained memory after full cycle is negligible (< 1 MB)
        Assert.True(leak < 1024 * 1024, $"Retained memory after lifecycle: {leak} bytes");
    }
}
