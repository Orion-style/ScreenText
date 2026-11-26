using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Xunit;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Settings;
using ZenithApp.Modules.StickyNotes;

namespace ZenithApp.Tests;

public class StickyNotesModuleTests : IDisposable
{
    private sealed class NullLogger : IAppLogger
    {
        public void Log(LogLevel level, string message, Exception? exception = null) { }
    }

    private sealed class FakeHotkeyService : IHotkeyService
    {
        public Dictionary<string, (KeyModifiers Modifiers, Key Key, Action Action)> Registered { get; } = new();
        public HashSet<string> FailingIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool RegisterHotkey(string id, KeyModifiers modifiers, Key key, Action action)
        {
            if (FailingIds.Contains(id))
            {
                return false;
            }
            Registered[id] = (modifiers, key, action);
            return true;
        }

        public bool UnregisterHotkey(string id) => Registered.Remove(id);
        public void UnregisterAll() => Registered.Clear();
        public void Dispose() => Registered.Clear();
    }

    private readonly string _tempDir;
    private readonly string _settingsPath;
    private readonly string _notesPath;

    public StickyNotesModuleTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ZenithStickyNotesTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "settings.json");
        _notesPath = Path.Combine(_tempDir, "sticky_notes.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failure
        }
    }

    private (StickyNotesModule Module, SettingsService Settings, FakeHotkeyService Hotkeys, StickyNotesStorageService Storage) CreateSut()
    {
        var logger = new NullLogger();
        var settings = new SettingsService(logger, _settingsPath);
        var hotkeys = new FakeHotkeyService();
        var storage = new StickyNotesStorageService(logger, _notesPath);
        var module = new StickyNotesModule(logger, settings, hotkeys, storage);
        return (module, settings, hotkeys, storage);
    }

    [Fact]
    public void StorageService_SaveAndLoad_PreservesAllStickyNoteProperties()
    {
        var storage = new StickyNotesStorageService(new NullLogger(), _notesPath);
        var id = Guid.NewGuid();
        var note = new StickyNoteItem
        {
            Id = id,
            Content = "Не забыть оплатить сервер\nПроверить логи",
            Left = 340,
            Top = 180,
            Width = 310,
            Height = 240,
            ColorHex = "#1B2838",
            Opacity = 0.85,
            FontSize = 16,
            IsPinnedTopmost = true,
            IsClickThrough = true,
            IsCollapsed = true,
            IsVisible = true
        };

        storage.Save(new[] { note });
        var loaded = storage.Load();

        Assert.Single(loaded);
        var item = loaded[0];
        Assert.Equal(id, item.Id);
        Assert.Equal("Не забыть оплатить сервер\nПроверить логи", item.Content);
        Assert.Equal(340, item.Left);
        Assert.Equal(180, item.Top);
        Assert.Equal(310, item.Width);
        Assert.Equal(240, item.Height);
        Assert.Equal("#1B2838", item.ColorHex);
        Assert.Equal(0.85, item.Opacity);
        Assert.Equal(16, item.FontSize);
        Assert.True(item.IsPinnedTopmost);
        Assert.True(item.IsClickThrough);
        Assert.True(item.IsCollapsed);
        Assert.True(item.IsVisible);
    }

    [Fact]
    public void StorageService_CorruptedFile_BacksUpAndReturnsEmptyList()
    {
        File.WriteAllText(_notesPath, "{ invalid json !!!");
        var storage = new StickyNotesStorageService(new NullLogger(), _notesPath);

        var loaded = storage.Load();

        Assert.Empty(loaded);
        var backups = Directory.GetFiles(_tempDir, "sticky_notes.json.corrupted.*");
        Assert.NotEmpty(backups);
    }

    [Fact]
    public void BoundsHelper_ClampsOffscreenCoordinates_WhenSecondaryMonitorDisconnected()
    {
        var primaryOnly = new Rect(0, 0, 1920, 1080);

        var (left, top, width, height) = StickyNoteBoundsHelper.ClampToVirtualScreen(
            left: 2600,
            top: -200,
            width: 280,
            height: 220,
            virtualScreen: primaryOnly);

        Assert.InRange(left, -216, 1920 - 64);
        Assert.InRange(top, 0, 1080 - StickyNoteItem.HeaderHeight);
        Assert.Equal(280, width);
        Assert.Equal(220, height);
    }

    [Fact]
    public async Task Module_CreateToggleClickThroughAndDelete_PersistsAcrossReload()
    {
        var (module, settings, hotkeys, storage) = CreateSut();
        await module.EnableAsync();

        Assert.True(hotkeys.Registered.ContainsKey(StickyNotesModule.HotkeyIdCreate));
        Assert.True(hotkeys.Registered.ContainsKey(StickyNotesModule.HotkeyIdToggleVisibility));

        var created = module.CreateNote("Важная заметка");
        Assert.Single(module.GetNotesSnapshot());
        Assert.Equal("Важная заметка", storage.Load()[0].Content);

        module.ToggleNoteClickThrough(created.Id);
        Assert.True(storage.Load()[0].IsClickThrough);

        var visibleAfterHide = module.ToggleAllVisibility();
        Assert.False(visibleAfterHide);
        Assert.True(settings.Current.StickyNotes.AllNotesHidden);

        module.DeleteNote(created.Id);
        Assert.Empty(module.GetNotesSnapshot());
        Assert.Empty(storage.Load());

        await module.DisableAsync();
        Assert.Empty(hotkeys.Registered);
    }

    [Fact]
    public async Task Module_HotkeyConflict_ReportsErrorMessageWithoutFailingEnable()
    {
        var (module, _, hotkeys, _) = CreateSut();
        hotkeys.FailingIds.Add(StickyNotesModule.HotkeyIdCreate);

        var enabled = await module.EnableAsync();

        Assert.True(enabled);
        Assert.NotNull(module.CreateHotkeyError);
        Assert.Null(module.ToggleVisibilityHotkeyError);

        await module.DisableAsync();
    }
}
