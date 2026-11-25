using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using Xunit;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Modules.NextSession;

namespace ZenithApp.Tests;

public class NextSessionModuleTests
{
    private sealed class FakeBootTimeProvider : ISystemBootTimeProvider
    {
        public DateTime BootTimeUtc { get; set; } = new DateTime(2026, 11, 25, 20, 0, 0, DateTimeKind.Utc);
        public DateTime GetCurrentBootTimestampUtc() => BootTimeUtc;
    }

    private sealed class FakeCatalogService : INextSessionProgramCatalogService
    {
        public List<NextSessionProgramItem> Items { get; } = new();

        public IReadOnlyList<NextSessionProgramItem> LoadAvailablePrograms(
            IEnumerable<NextSessionProgramEntry>? customPrograms = null,
            IEnumerable<NextSessionProgramEntry>? pendingPrograms = null)
        {
            return Items.ToList();
        }

        public ImageSource? ResolveIcon(string? executablePath, string? processName = null) => null;
    }

    private sealed class FakeLauncherService : INextSessionLauncherService
    {
        public List<NextSessionProgramEntry> LaunchedEntries { get; } = new();
        public HashSet<string> FailingNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Message, int LaunchedCount, IReadOnlyList<string> Failed)> Notifications { get; } = new();
        public bool RestartRequested { get; private set; }

        public bool LaunchProgram(NextSessionProgramEntry entry)
        {
            LaunchedEntries.Add(entry);
            return !FailingNames.Contains(entry.Name);
        }

        public void ShowSessionNotification(string message, int launchedCount, IReadOnlyList<string> failedPrograms)
        {
            Notifications.Add((message, launchedCount, failedPrograms.ToList()));
        }

        public bool RequestSystemRestart()
        {
            RestartRequested = true;
            return true;
        }
    }

    [Fact]
    public void NextSessionSettings_DefaultValues_AreCorrect()
    {
        var settings = new NextSessionSettings();

        Assert.Equal(KeyModifiers.Control | KeyModifiers.Alt, settings.HotkeyModifiers);
        Assert.Equal(Key.N, settings.HotkeyKey);
        Assert.True(settings.OnlyAfterSystemReboot);
        Assert.False(settings.TriggerOnResumeFromSleep);
        Assert.False(settings.HasPendingPlan);
        Assert.Equal(3, settings.StartupDelaySeconds);
        Assert.Empty(settings.PendingPrograms);
    }

    [Fact]
    public async Task NextSessionModule_EnableAndDisable_RegistersAndUnregistersHotkey()
    {
        var logger = new TestLogger();
        var settingsService = new TestSettingsService();
        var hotkeyService = new TestHotkeyService();
        var catalog = new FakeCatalogService();
        var launcher = new FakeLauncherService();
        var boot = new FakeBootTimeProvider();

        using var module = new NextSessionModule(logger, settingsService, hotkeyService, catalog, launcher, boot);

        Assert.Equal("next_session", module.Id);
        Assert.True(await module.EnableAsync());
        Assert.True(hotkeyService.RegisteredHotkeys.ContainsKey(NextSessionModule.HotkeyId));

        await module.DisableAsync();
        Assert.False(hotkeyService.RegisteredHotkeys.ContainsKey(NextSessionModule.HotkeyId));
    }

    [Fact]
    public void NextSessionModule_SavePlanAndClearPlan_UpdatesSettingsAndEvents()
    {
        var logger = new TestLogger();
        var settingsService = new TestSettingsService();
        var hotkeyService = new TestHotkeyService();
        var catalog = new FakeCatalogService();
        var launcher = new FakeLauncherService();
        var boot = new FakeBootTimeProvider();

        using var module = new NextSessionModule(logger, settingsService, hotkeyService, catalog, launcher, boot);

        int eventCount = 0;
        module.PlanStateChanged += () => eventCount++;

        var programs = new[]
        {
            new NextSessionProgramEntry { Name = "VS Code", ExecutablePath = @"C:\Apps\Code.exe", ProcessName = "Code" }
        };

        module.SavePlan("Доделать отчёт", programs);

        Assert.True(module.Settings.HasPendingPlan);
        Assert.Equal("Доделать отчёт", module.Settings.PendingMessage);
        Assert.Single(module.Settings.PendingPrograms);
        Assert.Equal(boot.BootTimeUtc, module.Settings.PlannedBootTimestampUtc);
        Assert.Equal(1, eventCount);

        module.ClearPlan();

        Assert.False(module.Settings.HasPendingPlan);
        Assert.Equal(string.Empty, module.Settings.PendingMessage);
        Assert.Empty(module.Settings.PendingPrograms);
        Assert.Null(module.Settings.PlannedBootTimestampUtc);
        Assert.Equal(2, eventCount);
    }

    [Fact]
    public void NextSessionModule_ShouldExecutePendingPlanOnStartup_RespectsRebootAndShutdownFlags()
    {
        var logger = new TestLogger();
        var settingsService = new TestSettingsService();
        var hotkeyService = new TestHotkeyService();
        var catalog = new FakeCatalogService();
        var launcher = new FakeLauncherService();
        var boot = new FakeBootTimeProvider();

        using var module = new NextSessionModule(logger, settingsService, hotkeyService, catalog, launcher, boot);

        module.SavePlan("Напомнить утром", Array.Empty<NextSessionProgramEntry>());

        // Без перезагрузки план не должен срабатывать
        Assert.False(module.ShouldExecutePendingPlanOnStartup());

        // Если зафиксирован Fast Startup shutdown (SessionEnding) -> должен сработать
        module.Settings.SystemShutdownObserved = true;
        Assert.True(module.ShouldExecutePendingPlanOnStartup());

        // Если изменилось время загрузки ОС -> должен сработать
        module.Settings.SystemShutdownObserved = false;
        boot.BootTimeUtc = boot.BootTimeUtc.AddMinutes(10);
        Assert.True(module.ShouldExecutePendingPlanOnStartup());

        // Если OnlyAfterSystemReboot отключён -> срабатывает даже без перезагрузки
        boot.BootTimeUtc = module.Settings.PlannedBootTimestampUtc!.Value;
        module.Settings.OnlyAfterSystemReboot = false;
        Assert.True(module.ShouldExecutePendingPlanOnStartup());
    }

    [Fact]
    public async Task NextSessionModule_ExecutePendingPlanAsync_LaunchesNotifiesAndClearsPlan()
    {
        var logger = new TestLogger();
        var settingsService = new TestSettingsService();
        settingsService.Current.NextSession.LaunchIntervalMs = 50;
        var hotkeyService = new TestHotkeyService();
        var catalog = new FakeCatalogService();
        var launcher = new FakeLauncherService();
        launcher.FailingNames.Add("MissingApp");
        var boot = new FakeBootTimeProvider();

        using var module = new NextSessionModule(logger, settingsService, hotkeyService, catalog, launcher, boot);

        module.SavePlan(
            "Проверить почту",
            new[]
            {
                new NextSessionProgramEntry { Name = "Telegram", ExecutablePath = @"C:\Apps\Telegram.exe" },
                new NextSessionProgramEntry { Name = "MissingApp", ExecutablePath = @"C:\Apps\Missing.exe" }
            });

        var executed = await module.ExecutePendingPlanAsync(force: true);

        Assert.True(executed);
        Assert.Equal(2, launcher.LaunchedEntries.Count);
        Assert.Single(launcher.Notifications);
        Assert.Equal("Проверить почту", launcher.Notifications[0].Message);
        Assert.Equal(1, launcher.Notifications[0].LaunchedCount);
        Assert.Equal(new[] { "MissingApp" }, launcher.Notifications[0].Failed);

        // После выполнения план должен быть очищен
        Assert.False(module.Settings.HasPendingPlan);
        Assert.Empty(module.Settings.PendingPrograms);
        Assert.Equal(string.Empty, module.Settings.PendingMessage);
        Assert.NotNull(module.Settings.LastExecutedAtUtc);
    }

    [Fact]
    public void NextSessionWindowViewModel_FilterHighlightSelectRunningAndSave_WorksCorrectly()
    {
        var logger = new TestLogger();
        var settingsService = new TestSettingsService();
        var hotkeyService = new TestHotkeyService();
        var catalog = new FakeCatalogService();
        catalog.Items.Add(new NextSessionProgramItem
        {
            Key = "CODE",
            Name = "Visual Studio Code",
            ProcessName = "Code",
            ExecutablePath = @"C:\Apps\Code.exe",
            IsRunning = true
        });
        catalog.Items.Add(new NextSessionProgramItem
        {
            Key = "FIGMA",
            Name = "Figma",
            ProcessName = "Figma",
            ExecutablePath = @"C:\Apps\Figma.exe",
            IsRunning = false
        });

        var launcher = new FakeLauncherService();
        using var module = new NextSessionModule(logger, settingsService, hotkeyService, catalog, launcher);
        using var vm = new NextSessionWindowViewModel(module, catalog, loadImmediately: true);

        Assert.Equal(2, vm.FilteredPrograms.Count);
        Assert.Equal(0, vm.SelectedCount);

        // Фильтрация по поиску
        vm.SearchText = "fig";
        Assert.Single(vm.FilteredPrograms);
        Assert.Equal("Figma", vm.FilteredPrograms[0].Name);

        vm.SearchText = string.Empty;
        Assert.Equal(2, vm.FilteredPrograms.Count);

        // Подсветка/выбор открытых сейчас программ
        vm.SelectRunningCommand.Execute(null);
        Assert.Equal(1, vm.SelectedCount);
        Assert.True(vm.FilteredPrograms.First(i => i.Key == "CODE").IsSelected);

        // Добавление пользовательского файла сразу выбирает его
        vm.AddCustomProgram(@"C:\Tools\MyTool.exe");
        Assert.Equal(2, vm.SelectedCount);
        Assert.Equal(3, vm.FilteredPrograms.Count);

        vm.Message = "Открыть рабочий проект";
        vm.SavePlan(showToast: false);

        Assert.True(module.Settings.HasPendingPlan);
        Assert.Equal("Открыть рабочий проект", module.Settings.PendingMessage);
        Assert.Equal(2, module.Settings.PendingPrograms.Count);
        Assert.Single(module.Settings.CustomPrograms);
    }
}
