using System.IO;
using System.Windows;
using Xunit;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;
using ZenithApp.Modules.DynamicIsland;
using ZenithApp.Modules.NextSession;
using ZenithApp.Modules.Screenshot;

namespace ZenithApp.Tests;

public class DynamicIslandTests
{
    private sealed class FakeIslandHost : IDynamicIslandHost
    {
        public List<(string Message, string? Title, ToastNotificationType Type, int Duration)> Notifications { get; } = new();
        public List<(int DelaySeconds, IReadOnlyList<string> Programs, string? Note)> Countdowns { get; } = new();
        public NextSessionCountdownOutcome CountdownResult { get; set; } = NextSessionCountdownOutcome.Completed;
        public int CloseCount { get; private set; }
        public int RefreshShelfCount { get; private set; }
        public int ExpandCount { get; private set; }
        public DynamicIslandSettings? LastAppliedSettings { get; private set; }
        public IslandMediaState? LastMediaState { get; private set; }

        public void ShowNotification(string message, string? title, ToastNotificationType type, int durationSeconds)
        {
            Notifications.Add((message, title, type, durationSeconds));
        }

        public Task<NextSessionCountdownOutcome> WaitWithCountdownAsync(
            int delaySeconds,
            IReadOnlyList<string> programNames,
            string? messagePreview,
            CancellationToken cancellationToken)
        {
            Countdowns.Add((delaySeconds, programNames.ToList(), messagePreview));
            return Task.FromResult(CountdownResult);
        }

        public void UpdateMediaState(IslandMediaState state) => LastMediaState = state;
        public void RefreshShelf() => RefreshShelfCount++;
        public void ApplySettings(DynamicIslandSettings settings) => LastAppliedSettings = settings;
        public void ExpandDrawer() => ExpandCount++;
        public void CloseHost() => CloseCount++;
    }

    [Fact]
    public void DynamicIslandSettings_Defaults_AreValid()
    {
        var settings = new AppSettings();

        Assert.True(settings.IsModuleEnabled(DynamicIslandModule.ModuleId));
        Assert.True(settings.DynamicIsland.ShowAppNotifications);
        Assert.True(settings.DynamicIsland.ShowNextSessionCountdown);
        Assert.True(settings.DynamicIsland.ShowMediaControls);
        Assert.False(settings.DynamicIsland.AutoHideWhenIdle);
        Assert.True(settings.DynamicIsland.HideInFullscreen);
        Assert.False(settings.DynamicIsland.RemoveShelfItemAfterDrag);
        Assert.Equal(0, settings.DynamicIsland.TopMarginPx);
    }

    [Fact]
    public async Task DynamicIslandModule_EnableAndDisable_RoutesAndUnroutesNotifications()
    {
        var logger = new TestLogger();
        var settingsService = new TestSettingsService();
        settingsService.Current.DynamicIsland.ShowMediaControls = false;
        var host = new FakeIslandHost();
        var shelf = new DynamicIslandShelfService(logger);

        using var module = new DynamicIslandModule(logger, settingsService, shelf, customHostFactory: () => host);

        try
        {
            // When disabled, interceptor is not active
            Assert.False(module.TryInterceptNotification("Msg", "Title", ToastNotificationType.Info, 3));

            Assert.True(await module.EnableAsync());
            Assert.True(((INextSessionCountdownPresenter)module).IsActive);

            // NotificationToast routes to island when module is enabled
            NotificationToast.Show("Снимок сохранён", "Скриншот", ToastNotificationType.Success, 4);
            Assert.Single(host.Notifications);
            Assert.Equal("Снимок сохранён", host.Notifications[0].Message);
            Assert.Equal("Скриншот", host.Notifications[0].Title);
            Assert.Equal(ToastNotificationType.Success, host.Notifications[0].Type);
            Assert.Equal(4, host.Notifications[0].Duration);

            // When ShowAppNotifications is disabled in settings, interceptor returns false (normal toast fallback)
            settingsService.Current.DynamicIsland.ShowAppNotifications = false;
            Assert.False(module.TryInterceptNotification("Fallback", "Title", ToastNotificationType.Info, 2));

            await module.DisableAsync();
            Assert.Equal(1, host.CloseCount);
            Assert.False(((INextSessionCountdownPresenter)module).IsActive);
            Assert.False(module.TryInterceptNotification("AfterDisable", "Title", ToastNotificationType.Info, 2));
        }
        finally
        {
            NotificationToast.SetInterceptor(null);
        }
    }

    [Fact]
    public async Task NextSessionLauncherService_WhenIslandCountdownActive_DelegatesToIsland()
    {
        var logger = new TestLogger();
        var settingsService = new TestSettingsService();
        settingsService.Current.DynamicIsland.ShowMediaControls = false;
        var host = new FakeIslandHost { CountdownResult = NextSessionCountdownOutcome.RunNow };
        var shelf = new DynamicIslandShelfService(logger);

        using var islandModule = new DynamicIslandModule(logger, settingsService, shelf, customHostFactory: () => host);
        await islandModule.EnableAsync();

        var launcher = new NextSessionLauncherService(logger, islandModule);
        var outcome = await launcher.WaitWithCountdownAsync(
            5,
            new[] { "Chrome", "Discord" },
            "Продолжить работу",
            CancellationToken.None);

        Assert.Equal(NextSessionCountdownOutcome.RunNow, outcome);
        Assert.Single(host.Countdowns);
        Assert.Equal(5, host.Countdowns[0].DelaySeconds);
        Assert.Equal(2, host.Countdowns[0].Programs.Count);
        Assert.Equal("Продолжить работу", host.Countdowns[0].Note);

        await islandModule.DisableAsync();
    }

    [Fact]
    public void ShelfService_AddFilesAndText_DeduplicatesPersistsAndBuildsDragData()
    {
        var logger = new TestLogger();
        var tempDir = Path.Combine(Path.GetTempPath(), "ZenithIslandTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileA = Path.Combine(tempDir, "notes.txt");
        var fileB = Path.Combine(tempDir, "archive.bin");
        var shelfJson = Path.Combine(tempDir, "shelf.json");
        File.WriteAllText(fileA, "hello world");
        File.WriteAllBytes(fileB, new byte[2048]);

        try
        {
            var shelf = new DynamicIslandShelfService(logger, shelfJson);
            Assert.Equal(2, shelf.AddFiles(new[] { fileA, fileB, Path.Combine(tempDir, "missing.txt") }));
            Assert.True(shelf.AddText("https://github.com/example/repo"));
            Assert.True(shelf.AddText("Важный фрагмент текста"));
            Assert.False(shelf.AddText("   "));

            // Deduplicating same file moves it to top without creating duplicates
            Assert.Equal(1, shelf.AddFiles(new[] { fileA }));
            var items = shelf.GetItems();
            Assert.Equal(4, items.Count);
            Assert.Equal("notes.txt", items[0].Title);
            Assert.Equal(IslandShelfItemKind.File, items[0].Kind);
            Assert.Contains(items, i => i.Kind == IslandShelfItemKind.Link);
            Assert.Contains(items, i => i.Kind == IslandShelfItemKind.Text);

            // Drag-out DataObject checks
            var fileData = DynamicIslandShelfService.CreateDataObjectForItem(items[0]);
            Assert.NotNull(fileData);
            Assert.True(fileData!.ContainsFileDropList());
            Assert.Contains(fileA, fileData.GetFileDropList().Cast<string>());

            var textItem = items.First(i => i.Kind == IslandShelfItemKind.Text);
            var textData = DynamicIslandShelfService.CreateDataObjectForItem(textItem);
            Assert.NotNull(textData);
            Assert.Equal("Важный фрагмент текста", textData!.GetData(DataFormats.UnicodeText));

            var allFilesData = shelf.CreateDataObjectForAllFiles();
            Assert.NotNull(allFilesData);
            Assert.Equal(2, allFilesData!.GetFileDropList().Count);

            var allTextData = shelf.CreateDataObjectForAllText();
            Assert.NotNull(allTextData);
            var combined = allTextData!.GetData(DataFormats.UnicodeText) as string;
            Assert.Contains("https://github.com/example/repo", combined);
            Assert.Contains("Важный фрагмент текста", combined);

            // Reload from disk verifies persistence and missing-file pruning
            File.Delete(fileB);
            var reloaded = new DynamicIslandShelfService(logger, shelfJson);
            Assert.Equal(3, reloaded.Count);

            reloaded.RemoveItem(reloaded.GetItems()[0].Id);
            Assert.Equal(2, reloaded.Count);

            reloaded.Clear();
            Assert.Equal(0, reloaded.Count);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ModuleManager_RecognizesDynamicIslandModule()
    {
        var logger = new TestLogger();
        var settingsService = new TestSettingsService();
        settingsService.Current.DynamicIsland.ShowMediaControls = false;
        var host = new FakeIslandHost();
        var module = new DynamicIslandModule(logger, settingsService, new DynamicIslandShelfService(logger), customHostFactory: () => host);

        using var manager = new ModuleManager(logger, settingsService, new IFeatureModule[] { module });
        await manager.InitializeModulesAsync();

        Assert.True(module.IsEnabled);
        Assert.True(await manager.SetModuleStateAsync(DynamicIslandModule.ModuleId, false));
        Assert.False(module.IsEnabled);
        NotificationToast.SetInterceptor(null);
    }
}
