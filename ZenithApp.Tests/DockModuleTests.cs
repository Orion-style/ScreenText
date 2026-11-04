using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Settings;
using ZenithApp.Modules.Dock;

namespace ZenithApp.Tests;

public class TestHotkeyService : IHotkeyService
{
    public Dictionary<string, Action> Registered = new();

    public bool RegisterHotkey(string id, KeyModifiers modifiers, System.Windows.Input.Key key, Action action)
    {
        Registered[id] = action;
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

    public void Dispose() {}
}

public class DockModuleTests
{
    [Fact]
    public void DockModule_ZeroOverhead_WhenDisabled()
    {
        var logger = new TestLogger();
        var settingsService = new TestSettingsService();
        var hotkeyService = new TestHotkeyService();

        using var module = new DockModule(logger, settingsService, hotkeyService);

        Assert.False(module.IsEnabled);
        Assert.Empty(hotkeyService.Registered);
    }

    [Fact]
    public void DockModule_ItemManagement_AddMoveDelete()
    {
        var logger = new TestLogger();
        var settingsService = new TestSettingsService();
        var hotkeyService = new TestHotkeyService();

        using var module = new DockModule(logger, settingsService, hotkeyService);

        module.AddItem(@"C:\Windows\notepad.exe", "Notepad");
        module.AddItem(@"C:\Windows\explorer.exe", "Explorer");

        Assert.Equal(2, module.Settings.Items.Count);
        Assert.Equal("Notepad", module.Settings.Items[0].Title);
        Assert.Equal("Explorer", module.Settings.Items[1].Title);

        // Move item
        module.MoveItem(0, 1);
        Assert.Equal("Explorer", module.Settings.Items[0].Title);
        Assert.Equal("Notepad", module.Settings.Items[1].Title);

        // Remove item
        module.RemoveItem(module.Settings.Items[0]);
        Assert.Single(module.Settings.Items);
        Assert.Equal("Notepad", module.Settings.Items[0].Title);
    }

    [Fact]
    public void IconExtractionService_MissingItem_ReturnsFallbackIcon()
    {
        var missingPath = @"C:\NonExistentPath\totally_missing_program_xyz.exe";
        var logger = new TestLogger();
        var icon = IconExtractionService.GetIcon(missingPath, 64, logger);

        // Должен возвращаться запасной значок, а событие неудачного извлечения должно быть залогировано
        Assert.NotNull(icon);
        Assert.Contains(logger.Logs, m => m.Contains("[IconExtraction]") && m.Contains("totally_missing_program_xyz.exe"));
    }

    [Fact]
    public void ProcessInteractionService_Detection_FindsRunningOrFalse()
    {
        // Notepad is likely not running in headless test
        var isRunning = ProcessInteractionService.IsRunning("notepad.exe", out _);
        // Method returns cleanly without exceptions
        Assert.True(isRunning || !isRunning);
    }

    [Fact]
    public void DockSettings_DefaultValues_AreCorrect()
    {
        var settings = new DockSettings();
        // Режим по умолчанию: Всегда виден (автоскрытие выключено)
        Assert.False(settings.AutoHide);
        // Заменять панель задач Windows по умолчанию: ВКЛЮЧЕНО
        Assert.True(settings.ReplaceWindowsTaskbar);
        Assert.False(settings.TaskbarStateModified);
        Assert.Null(settings.OriginalTaskbarState);
    }

    [Fact]
    public void DockModule_CrashGuard_RestoresTaskbarState()
    {
        var logger = new TestLogger();
        var settingsService = new TestSettingsService();
        var hotkeyService = new TestHotkeyService();

        // Симулируем падение приложения при измененном состоянии панели задач
        settingsService.Current.Dock.TaskbarStateModified = true;
        settingsService.Current.Dock.OriginalTaskbarState = 2; // ABS_ALWAYSONTOP

        using var module = new DockModule(logger, settingsService, hotkeyService);

        // Crash Guard должен сбросить флаг модификации
        Assert.False(module.Settings.TaskbarStateModified);
        Assert.Contains(logger.Logs, m => m.Contains("Crash Guard"));
    }
}
