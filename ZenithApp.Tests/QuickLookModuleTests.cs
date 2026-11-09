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
        Assert.Equal(QuickLookContentType.GenericCard, ContentDetector.DetectContentType("file.txt", settings));
        Assert.Equal(QuickLookContentType.Pdf, ContentDetector.DetectContentType("doc.pdf", settings));
        Assert.Equal(QuickLookContentType.GenericCard, ContentDetector.DetectContentType("archive.zip", settings));
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
}
