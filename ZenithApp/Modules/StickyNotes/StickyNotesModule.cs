using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;

namespace ZenithApp.Modules.StickyNotes;

public sealed class StickyNotesModule : BaseFeatureModule
{
    public const string ModuleId = "sticky_notes";
    public const string HotkeyIdCreate = "sticky_notes_create";
    public const string HotkeyIdToggleVisibility = "sticky_notes_toggle_visibility";

    private readonly ISettingsService _settingsService;
    private readonly IHotkeyService _hotkeyService;
    private readonly IStickyNotesStorageService _storageService;

    private readonly object _stateLock = new();
    private readonly Dictionary<Guid, StickyNoteWindow> _openWindows = new();
    private List<StickyNoteItem> _notes = new();
    private Timer? _saveDebounceTimer;
    private bool _hasPendingSave;
    private bool _systemEventsSubscribed;

    public StickyNotesModule(
        IAppLogger logger,
        ISettingsService settingsService,
        IHotkeyService hotkeyService,
        IStickyNotesStorageService storageService) : base(logger)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _storageService = storageService;
    }

    public override string Id => ModuleId;
    public override string Name => "Стикеры на экран";
    public override string Description => "Постоянные заметки поверх приложений, сохраняемые после перезагрузки, с режимом пропуска кликов.";

    public StickyNotesSettings Settings => _settingsService.Current.StickyNotes;

    public string? CreateHotkeyError { get; private set; }
    public string? ToggleVisibilityHotkeyError { get; private set; }

    public event Action? HotkeyStatusChanged;
    public event Action? NotesChanged;

    protected override Task OnEnableAsync()
    {
        lock (_stateLock)
        {
            _notes = _storageService.Load().Select(n => n.Clone()).ToList();
        }

        RegisterHotkeys();
        SubscribeSystemEvents();

        if (!Settings.AllNotesHidden)
        {
            RunOnUiThread(() => SyncAllWindows(focusNoteId: null));
        }

        NotesChanged?.Invoke();
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        UnsubscribeSystemEvents();
        UnregisterHotkeys();
        FlushSave();
        RunOnUiThread(CloseAllWindows);
        NotesChanged?.Invoke();
        return Task.CompletedTask;
    }

    public void RegisterHotkeys()
    {
        UnregisterHotkeys();

        var config = Settings;

        var createOk = _hotkeyService.RegisterHotkey(
            HotkeyIdCreate,
            config.CreateHotkeyModifiers,
            config.CreateHotkeyKey,
            () => RunOnUiThread(() => CreateNote()));

        CreateHotkeyError = createOk
            ? null
            : $"Горячая клавиша {FormatHotkey(config.CreateHotkeyModifiers, config.CreateHotkeyKey)} уже занята.";

        var toggleOk = _hotkeyService.RegisterHotkey(
            HotkeyIdToggleVisibility,
            config.ToggleVisibilityHotkeyModifiers,
            config.ToggleVisibilityHotkeyKey,
            () => RunOnUiThread(() => ToggleAllVisibility()));

        ToggleVisibilityHotkeyError = toggleOk
            ? null
            : $"Горячая клавиша {FormatHotkey(config.ToggleVisibilityHotkeyModifiers, config.ToggleVisibilityHotkeyKey)} уже занята.";

        HotkeyStatusChanged?.Invoke();
    }

    public void UnregisterHotkeys()
    {
        _hotkeyService.UnregisterHotkey(HotkeyIdCreate);
        _hotkeyService.UnregisterHotkey(HotkeyIdToggleVisibility);
        CreateHotkeyError = null;
        ToggleVisibilityHotkeyError = null;
        HotkeyStatusChanged?.Invoke();
    }

    public IReadOnlyList<StickyNoteItem> GetNotesSnapshot()
    {
        lock (_stateLock)
        {
            if (!IsEnabled && _notes.Count == 0)
            {
                _notes = _storageService.Load().Select(n => n.Clone()).ToList();
            }

            return _notes.Select(n => n.Clone()).ToList();
        }
    }

    public StickyNoteItem CreateNote(string? initialContent = null)
    {
        EnsureNotesLoaded();

        StickyNoteItem created;
        lock (_stateLock)
        {
            var offsetStep = _notes.Count % 8;
            double left = 120 + offsetStep * 26;
            double top = 120 + offsetStep * 26;

            if (Application.Current != null)
            {
                var clamped = StickyNoteBoundsHelper.ClampToVirtualScreen(
                    left,
                    top,
                    StickyNoteItem.DefaultWidth,
                    StickyNoteItem.DefaultHeight,
                    StickyNoteBoundsHelper.GetCurrentVirtualScreen());
                left = clamped.Left;
                top = clamped.Top;
            }

            created = new StickyNoteItem
            {
                Id = Guid.NewGuid(),
                Content = initialContent ?? string.Empty,
                Left = left,
                Top = top,
                Width = StickyNoteItem.DefaultWidth,
                Height = StickyNoteItem.DefaultHeight,
                ColorHex = StickyNotePalette.Resolve(Settings.DefaultColorHex).Id,
                Opacity = Math.Clamp(Settings.DefaultOpacity, 0.35, 1.0),
                FontSize = Math.Clamp(Settings.DefaultFontSize, 10, 28),
                IsPinnedTopmost = Settings.DefaultTopmost,
                IsClickThrough = false,
                IsCollapsed = false,
                IsVisible = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            _notes.Add(created);
        }

        if (Settings.AllNotesHidden)
        {
            Settings.AllNotesHidden = false;
            _settingsService.Save();
        }

        SaveImmediately();

        if (IsEnabled)
        {
            RunOnUiThread(() => SyncAllWindows(focusNoteId: created.Id));
        }

        NotesChanged?.Invoke();
        return created.Clone();
    }

    public bool ToggleAllVisibility()
    {
        Settings.AllNotesHidden = !Settings.AllNotesHidden;
        _settingsService.Save();

        if (IsEnabled)
        {
            RunOnUiThread(() =>
            {
                if (Settings.AllNotesHidden)
                {
                    CloseAllWindows();
                }
                else
                {
                    SyncAllWindows(focusNoteId: null);
                }
            });
        }

        NotesChanged?.Invoke();
        return !Settings.AllNotesHidden;
    }

    public void SetNoteVisibility(Guid id, bool visible)
    {
        EnsureNotesLoaded();

        StickyNoteItem? target = null;
        lock (_stateLock)
        {
            target = _notes.FirstOrDefault(n => n.Id == id);
            if (target == null) return;
            target.IsVisible = visible;
            target.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        if (visible && Settings.AllNotesHidden)
        {
            Settings.AllNotesHidden = false;
            _settingsService.Save();
        }

        SaveImmediately();

        if (IsEnabled)
        {
            RunOnUiThread(() => SyncAllWindows(focusNoteId: visible ? id : null));
        }

        NotesChanged?.Invoke();
    }

    public void ToggleNoteClickThrough(Guid id)
    {
        EnsureNotesLoaded();

        lock (_stateLock)
        {
            var target = _notes.FirstOrDefault(n => n.Id == id);
            if (target == null) return;
            target.IsClickThrough = !target.IsClickThrough;
            target.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        SaveImmediately();

        if (IsEnabled)
        {
            RunOnUiThread(() =>
            {
                if (_openWindows.TryGetValue(id, out var window))
                {
                    window.RefreshFromModel();
                }
            });
        }

        NotesChanged?.Invoke();
    }

    public void ResetNotePosition(Guid id)
    {
        EnsureNotesLoaded();

        lock (_stateLock)
        {
            var target = _notes.FirstOrDefault(n => n.Id == id);
            if (target == null) return;
            target.Left = 140;
            target.Top = 140;
            target.IsCollapsed = false;
            target.IsVisible = true;
            target.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        if (Settings.AllNotesHidden)
        {
            Settings.AllNotesHidden = false;
            _settingsService.Save();
        }

        SaveImmediately();

        if (IsEnabled)
        {
            RunOnUiThread(() => SyncAllWindows(focusNoteId: id));
        }

        NotesChanged?.Invoke();
    }

    public void DeleteNote(Guid id)
    {
        EnsureNotesLoaded();

        bool removed;
        lock (_stateLock)
        {
            removed = _notes.RemoveAll(n => n.Id == id) > 0;
        }

        if (!removed) return;

        SaveImmediately();

        if (IsEnabled)
        {
            RunOnUiThread(() =>
            {
                if (_openWindows.TryGetValue(id, out var window))
                {
                    _openWindows.Remove(id);
                    window.CloseCleanly();
                }
            });
        }

        NotesChanged?.Invoke();
    }

    public void FlushSave()
    {
        bool shouldSave;
        lock (_stateLock)
        {
            _saveDebounceTimer?.Dispose();
            _saveDebounceTimer = null;
            shouldSave = _hasPendingSave;
            _hasPendingSave = false;
        }

        if (shouldSave)
        {
            SaveImmediately();
        }
    }

    private void EnsureNotesLoaded()
    {
        lock (_stateLock)
        {
            if (_notes.Count == 0 && !IsEnabled)
            {
                _notes = _storageService.Load().Select(n => n.Clone()).ToList();
            }
        }
    }

    private void ScheduleDebouncedSave()
    {
        lock (_stateLock)
        {
            _hasPendingSave = true;
            _saveDebounceTimer?.Dispose();
            _saveDebounceTimer = new Timer(_ =>
            {
                FlushSave();
                NotesChanged?.Invoke();
            }, null, 500, Timeout.Infinite);
        }
    }

    private void SaveImmediately()
    {
        List<StickyNoteItem> snapshot;
        lock (_stateLock)
        {
            _saveDebounceTimer?.Dispose();
            _saveDebounceTimer = null;
            _hasPendingSave = false;
            snapshot = _notes.Select(n => n.Clone()).ToList();
        }

        _storageService.Save(snapshot);
    }

    private void SyncAllWindows(Guid? focusNoteId)
    {
        if (Settings.AllNotesHidden || !IsEnabled)
        {
            CloseAllWindows();
            return;
        }

        List<StickyNoteItem> visibleNotes;
        lock (_stateLock)
        {
            visibleNotes = _notes.Where(n => n.IsVisible).ToList();
        }

        var visibleIds = visibleNotes.Select(n => n.Id).ToHashSet();

        foreach (var existingId in _openWindows.Keys.ToList())
        {
            if (!visibleIds.Contains(existingId))
            {
                var wnd = _openWindows[existingId];
                _openWindows.Remove(existingId);
                wnd.CloseCleanly();
            }
        }

        foreach (var note in visibleNotes)
        {
            if (_openWindows.TryGetValue(note.Id, out var existingWindow))
            {
                existingWindow.RefreshFromModel();
                if (focusNoteId == note.Id && !note.IsClickThrough)
                {
                    existingWindow.Activate();
                    existingWindow.NoteTextBox.Focus();
                }
                continue;
            }

            var window = new StickyNoteWindow(
                note,
                onChanged: _ => ScheduleDebouncedSave(),
                onDeleteRequested: DeleteNote,
                onHideRequested: id => SetNoteVisibility(id, false));

            _openWindows[note.Id] = window;
            window.Show();

            if (focusNoteId == note.Id && !note.IsClickThrough)
            {
                window.Activate();
                window.NoteTextBox.Focus();
            }
        }
    }

    private void CloseAllWindows()
    {
        foreach (var window in _openWindows.Values.ToList())
        {
            window.CloseCleanly();
        }
        _openWindows.Clear();
    }

    private void SubscribeSystemEvents()
    {
        if (_systemEventsSubscribed) return;
        try
        {
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.SessionEnding += OnSessionEnding;
            _systemEventsSubscribed = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[StickyNotes] Failed to subscribe to system events: {ex.Message}");
        }
    }

    private void UnsubscribeSystemEvents()
    {
        if (!_systemEventsSubscribed) return;
        try
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
        }
        catch
        {
            // Ignore cleanup failure
        }
        finally
        {
            _systemEventsSubscribed = false;
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            foreach (var window in _openWindows.Values)
            {
                window.RefreshFromModel();
            }
            ScheduleDebouncedSave();
        });
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        FlushSave();
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    public static string FormatHotkey(KeyModifiers modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Win)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }

    public override void Dispose()
    {
        FlushSave();
        base.Dispose();
    }
}
