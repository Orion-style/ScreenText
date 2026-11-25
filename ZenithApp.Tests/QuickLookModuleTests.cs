using System.IO;
using System.Text;
using ZenithApp.Core.Input;
using ZenithApp.Modules.QuickLook;

namespace ZenithApp.Tests;

public class QuickLookModuleTests
{
    [Fact]
    public void ContentDetector_CategorizesCorrectly()
    {
        var settings = new QuickLookSettings();

        Assert.Equal(QuickLookContentType.Image, ContentDetector.DetectContentType("photo.png", settings));
        Assert.Equal(QuickLookContentType.Image, ContentDetector.DetectContentType("test.jpg", settings));
        Assert.Equal(QuickLookContentType.Image, ContentDetector.DetectContentType("image.webp", settings));
        Assert.Equal(QuickLookContentType.Image, ContentDetector.DetectContentType("photo.jfif", settings));
        Assert.Equal(QuickLookContentType.Image, ContentDetector.DetectContentType("camera.heic", settings));
        Assert.Equal(QuickLookContentType.Image, ContentDetector.DetectContentType("graphic.avif", settings));
        Assert.Equal(QuickLookContentType.Image, ContentDetector.DetectContentType("logo.svg", settings));
        Assert.Equal(QuickLookContentType.Pdf, ContentDetector.DetectContentType("doc.pdf", settings));
        Assert.Equal(QuickLookContentType.Video, ContentDetector.DetectContentType("movie.mp4", settings));
        Assert.Equal(QuickLookContentType.Audio, ContentDetector.DetectContentType("track.mp3", settings));
        Assert.Equal(QuickLookContentType.Text, ContentDetector.DetectContentType("program.cs", settings));
        Assert.Equal(QuickLookContentType.Archive, ContentDetector.DetectContentType("archive.zip", settings));
        Assert.Equal(QuickLookContentType.GenericCard, ContentDetector.DetectContentType("document.docx", settings));
    }

    [Fact]
    public void ContentDetector_RespectsDisabledCategories()
    {
        var settings = new QuickLookSettings
        {
            EnableImages = false,
            EnableText = false,
            EnableArchives = false
        };

        Assert.Equal(QuickLookContentType.GenericCard, ContentDetector.DetectContentType("photo.png", settings));
        Assert.Equal(QuickLookContentType.GenericCard, ContentDetector.DetectContentType("image.webp", settings));
        Assert.Equal(QuickLookContentType.GenericCard, ContentDetector.DetectContentType("logo.svg", settings));
        Assert.Equal(QuickLookContentType.GenericCard, ContentDetector.DetectContentType("file.txt", settings));
        Assert.Equal(QuickLookContentType.Pdf, ContentDetector.DetectContentType("doc.pdf", settings));
        Assert.Equal(QuickLookContentType.GenericCard, ContentDetector.DetectContentType("archive.zip", settings));

        // When Images are disabled but Text is enabled, SVG falls back to Text preview
        var textOnlySettings = new QuickLookSettings
        {
            EnableImages = false,
            EnableText = true
        };
        Assert.Equal(QuickLookContentType.Text, ContentDetector.DetectContentType("logo.svg", textOnlySettings));
    }

    [Fact]
    public async Task TextFileReader_ReadsAndDetectsEncoding()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Привет, мир! Zenith Quick Look тестовый текст.", Encoding.UTF8);

            var result = await TextFileReader.ReadTextPreviewAsync(tempFile, 1024 * 1024);

            Assert.NotNull(result);
            Assert.Contains("Привет, мир!", result.Content);
            Assert.False(result.IsTruncated);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task TextFileReader_TruncatesLargeFiles()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var largeContent = new string('A', 5000);
            await File.WriteAllTextAsync(tempFile, largeContent);

            var result = await TextFileReader.ReadTextPreviewAsync(tempFile, 1000);

            Assert.NotNull(result);
            Assert.True(result.IsTruncated);
            Assert.Equal(1000, result.Content.Length);
            Assert.Equal(5000, result.OriginalSizeBytes);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task QuickLookModule_ZeroOverhead_WhenDisabled()
    {
        var logger = new TestLogger();
        using var hookService = new KeyboardHookService();
        var settingsService = new TestSettingsService();

        using var module = new QuickLookModule(logger, hookService, settingsService);

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
    public void LayoutHelper_FitsLargeScreenshotsInsideViewport_WithoutScrollAtDefaultZoom()
    {
        // Fullscreen 1920x1080 screenshot inside a ~1400x760 viewport
        var fhd = QuickLookLayoutHelper.ComputeImageDisplaySize(1920, 1080, 1400, 760, 1.0);
        Assert.False(fhd.NeedsScroll);
        Assert.True(fhd.Width <= 1400.01);
        Assert.True(fhd.Height <= 760.01);
        Assert.Equal(16.0 / 9.0, fhd.Width / fhd.Height, precision: 3);

        // 4K 3840x2160 screenshot inside a scaled 150% 1080p viewport (~990x500 DIPs)
        var uhd = QuickLookLayoutHelper.ComputeImageDisplaySize(3840, 2160, 990, 500, 1.0);
        Assert.False(uhd.NeedsScroll);
        Assert.True(uhd.Width <= 990.01);
        Assert.True(uhd.Height <= 500.01);
        Assert.Equal(16.0 / 9.0, uhd.Width / uhd.Height, precision: 3);
    }

    [Fact]
    public void LayoutHelper_DoesNotUpscaleSmallImageAtDefaultZoom_AndScrollsWhenZoomedIn()
    {
        var small = QuickLookLayoutHelper.ComputeImageDisplaySize(200, 200, 500, 340, 1.0);
        Assert.False(small.NeedsScroll);
        Assert.Equal(200, small.Width, precision: 2);
        Assert.Equal(200, small.Height, precision: 2);

        var zoomedIn = QuickLookLayoutHelper.ComputeImageDisplaySize(1920, 1080, 1200, 675, 2.0);
        Assert.True(zoomedIn.NeedsScroll);
        Assert.Equal(2400, zoomedIn.Width, precision: 1);
        Assert.Equal(1350, zoomedIn.Height, precision: 1);
    }

    [Fact]
    public void LayoutHelper_TargetWindowBounds_RespectsMonitorLimitsAndChrome()
    {
        // 1920x1080 monitor at 150% DPI scaling => ~1280x680 DIP work area, 80% max => 1024x544
        var bounds = QuickLookLayoutHelper.ComputeTargetWindowBounds(3840, 2160, 1280, 680, 80, extraWidth: 34, extraHeight: 76);
        Assert.True(bounds.MaxWidth <= 1024.01);
        Assert.True(bounds.MaxHeight <= 544.01);
        Assert.True(bounds.Width <= bounds.MaxWidth);
        Assert.True(bounds.Height <= bounds.MaxHeight);

        // Resulting content area preserves 16:9 closely
        double contentW = bounds.Width - 34;
        double contentH = bounds.Height - 76;
        Assert.Equal(16.0 / 9.0, contentW / contentH, precision: 2);
    }

    [Fact]
    public void LayoutHelper_PdfPageSizingAndRenderResolution_AreBoundedAndProportional()
    {
        // PDF page fits viewport width cleanly at 1.0x zoom
        var pdfFit = QuickLookLayoutHelper.ComputePdfPageDisplaySize(1400, 1980, 520, 1.0);
        Assert.False(pdfFit.NeedsScroll);
        Assert.Equal(520, pdfFit.Width, precision: 2);
        Assert.Equal(1980.0 / 1400.0, pdfFit.Height / pdfFit.Width, precision: 3);

        // Zoomed PDF enables horizontal scrolling
        var pdfZoom = QuickLookLayoutHelper.ComputePdfPageDisplaySize(1400, 1980, 520, 1.5);
        Assert.True(pdfZoom.NeedsScroll);
        Assert.Equal(780, pdfZoom.Width, precision: 2);

        // Render destination width for A4 (~794x1123) renders crisp >= 1400px, while huge CAD/banner pages cap safely
        uint a4Width = QuickLookLayoutHelper.ComputePdfRenderDestinationWidth(794, 1123, maxWidth: 1800, maxHeight: 2600);
        Assert.Equal(1400u, a4Width);

        uint hugeBannerWidth = QuickLookLayoutHelper.ComputePdfRenderDestinationWidth(500, 5000, maxWidth: 1800, maxHeight: 2600);
        Assert.Equal(260u, hugeBannerWidth);

        // High-DPI crisp target width renders A4 at >= 2000px
        uint crispA4Width = QuickLookLayoutHelper.ComputePdfRenderDestinationWidth(595, 842, maxWidth: 2800, maxHeight: 4000, targetWidth: 2200);
        Assert.Equal(1785u, crispA4Width); // 595 * 3.0 = 1785
        uint crisp96DpiA4Width = QuickLookLayoutHelper.ComputePdfRenderDestinationWidth(794, 1123, maxWidth: 2800, maxHeight: 4000, targetWidth: 2200);
        Assert.Equal(2200u, crisp96DpiA4Width);
    }

    [Fact]
    public void LayoutHelper_PdfWindowBounds_ExpandsNearlyFullscreenLikeBrowser()
    {
        // 1920x1040 work area (100% DPI) with default 80% setting -> PDF expands to 94%
        var fhdBounds = QuickLookLayoutHelper.ComputePdfWindowBounds(1920, 1040, maxWindowScreenPercent: 80);
        Assert.True(fhdBounds.Width >= 1920 * 0.93);
        Assert.True(fhdBounds.Height >= 1040 * 0.93);
        Assert.Equal(fhdBounds.Width, fhdBounds.MaxWidth);
        Assert.Equal(fhdBounds.Height, fhdBounds.MaxHeight);

        // 1280x680 work area (150% DPI) -> ~1203x639
        var scaledBounds = QuickLookLayoutHelper.ComputePdfWindowBounds(1280, 680, maxWindowScreenPercent: 80);
        Assert.True(scaledBounds.Width >= 1200);
        Assert.True(scaledBounds.Height >= 635);
    }
}
