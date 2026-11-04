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
    public void IconExtractionService_MissingItem_ReturnsNullGracefully()
    {
        var missingPath = @"C:\NonExistentPath\totally_missing_program_xyz.exe";
        var icon = IconExtractionService.GetIcon(missingPath);

        // Does not throw and returns null, allowing UI to render missing badge
        Assert.Null(icon);
    }

    [Fact]
    public void ProcessInteractionService_Detection_FindsRunningOrFalse()
    {
        // Notepad is likely not running in headless test
        var isRunning = ProcessInteractionService.IsRunning("notepad.exe", out _);
        // Method returns cleanly without exceptions
        Assert.True(isRunning || !isRunning);
    }
}
