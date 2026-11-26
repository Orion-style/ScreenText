using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Settings;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.StickyNotes;

public sealed class StickyNotePresetOption
{
    public StickyNotePresetOption(StickyNoteTheme theme, Brush backgroundBrush, Brush borderBrush)
    {
        Theme = theme;
        BackgroundBrush = backgroundBrush;
        BorderBrush = borderBrush;
    }

    public StickyNoteTheme Theme { get; }
    public string ColorHex => Theme.Id;
    public string Name => Theme.Name;
    public Brush BackgroundBrush { get; }
    public Brush BorderBrush { get; }
}

public sealed class StickyNoteListItemViewModel
{
    public StickyNoteListItemViewModel(StickyNoteItem item)
    {
        Id = item.Id;
        var singleLine = (item.Content ?? string.Empty)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
        PreviewText = string.IsNullOrEmpty(singleLine)
            ? "(Пустой стикер)"
            : (singleLine.Length > 70 ? singleLine[..70] + "..." : singleLine);

        var theme = StickyNotePalette.Resolve(item.ColorHex);
        AccentBrush = StickyNotePalette.CreateBorderBrush(theme);
        IsVisible = item.IsVisible;
        IsClickThrough = item.IsClickThrough;
        IsPinnedTopmost = item.IsPinnedTopmost;
        IsCollapsed = item.IsCollapsed;

        var tags = new List<string>
        {
            item.IsVisible ? "На экране" : "Скрыт",
            item.IsPinnedTopmost ? "Поверх окон" : "Обычный слой"
        };
        if (item.IsClickThrough) tags.Add("Сквозь клики");
        if (item.IsCollapsed) tags.Add("Свёрнут");
        StatusBadgeText = string.Join(" • ", tags);

        ToggleVisibilityText = item.IsVisible ? "Скрыть" : "Показать";
        ToggleClickThroughText = item.IsClickThrough ? "Разблок." : "Сквозь клики";
    }

    public Guid Id { get; }
    public string PreviewText { get; }
    public string StatusBadgeText { get; }
    public string ToggleVisibilityText { get; }
    public string ToggleClickThroughText { get; }
    public Brush AccentBrush { get; }
    public bool IsVisible { get; }
    public bool IsClickThrough { get; }
    public bool IsPinnedTopmost { get; }
    public bool IsCollapsed { get; }
}

public sealed class StickyNotesSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly StickyNotesModule _module;
    private readonly ISettingsService _settingsService;

    private string _createHotkeyText = string.Empty;
    private string _toggleVisibilityHotkeyText = string.Empty;
    private string? _createConflictMessage;
    private string? _toggleConflictMessage;
    private string _summaryText = string.Empty;
    private string _defaultColorHex = "#232730";
    private double _defaultOpacityPercent = 92;
    private double _defaultFontSize = 14;
    private bool _defaultTopmost = true;
    private bool _allNotesHidden;
    private bool _disposed;

    public StickyNotesSettingsViewModel(StickyNotesModule module, ISettingsService settingsService)
    {
        _module = module;
        _settingsService = settingsService;

        Notes = new ObservableCollection<StickyNoteListItemViewModel>();
        ColorPresets = new ObservableCollection<StickyNotePresetOption>(
            StickyNotePalette.Presets.Select(p => new StickyNotePresetOption(
                p,
                StickyNotePalette.CreateBackgroundBrush(p, 1.0),
                StickyNotePalette.CreateBorderBrush(p))));

        CreateNoteCommand = new RelayCommand(() => _module.CreateNote());
        ToggleAllVisibilityCommand = new RelayCommand(() => _module.ToggleAllVisibility());
        SelectDefaultColorCommand = new RelayCommand(param =>
        {
            if (param is string hex)
            {
                DefaultColorHex = hex;
            }
        });
        ToggleNoteVisibilityCommand = new RelayCommand(param =>
        {
            if (param is StickyNoteListItemViewModel item)
            {
                _module.SetNoteVisibility(item.Id, !item.IsVisible);
            }
        });
        ToggleNoteClickThroughCommand = new RelayCommand(param =>
        {
            if (param is StickyNoteListItemViewModel item)
            {
                _module.ToggleNoteClickThrough(item.Id);
            }
        });
        CenterNoteCommand = new RelayCommand(param =>
        {
            if (param is StickyNoteListItemViewModel item)
            {
                _module.ResetNotePosition(item.Id);
            }
        });
        DeleteNoteCommand = new RelayCommand(param =>
        {
            if (param is StickyNoteListItemViewModel item)
            {
                _module.DeleteNote(item.Id);
            }
        });

        _module.HotkeyStatusChanged += UpdateHotkeyConflicts;
        _module.NotesChanged += OnModuleNotesChanged;

        LoadFromSettings();
        RefreshNotes();
        UpdateHotkeyConflicts();
    }

    public ObservableCollection<StickyNoteListItemViewModel> Notes { get; }
    public ObservableCollection<StickyNotePresetOption> ColorPresets { get; }

    public ICommand CreateNoteCommand { get; }
    public ICommand ToggleAllVisibilityCommand { get; }
    public ICommand SelectDefaultColorCommand { get; }
    public ICommand ToggleNoteVisibilityCommand { get; }
    public ICommand ToggleNoteClickThroughCommand { get; }
    public ICommand CenterNoteCommand { get; }
    public ICommand DeleteNoteCommand { get; }

    public string CreateHotkeyText
    {
        get => _createHotkeyText;
        private set => SetProperty(ref _createHotkeyText, value);
    }

    public string ToggleVisibilityHotkeyText
    {
        get => _toggleVisibilityHotkeyText;
        private set => SetProperty(ref _toggleVisibilityHotkeyText, value);
    }

    public string? CreateConflictMessage
    {
        get => _createConflictMessage;
        private set
        {
            if (SetProperty(ref _createConflictMessage, value))
            {
                OnPropertyChanged(nameof(HasCreateConflict));
            }
        }
    }

    public bool HasCreateConflict => !string.IsNullOrEmpty(CreateConflictMessage);

    public string? ToggleConflictMessage
    {
        get => _toggleConflictMessage;
        private set
        {
            if (SetProperty(ref _toggleConflictMessage, value))
            {
                OnPropertyChanged(nameof(HasToggleConflict));
            }
        }
    }

    public bool HasToggleConflict => !string.IsNullOrEmpty(ToggleConflictMessage);

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public bool AllNotesHidden
    {
        get => _allNotesHidden;
        private set
        {
            if (SetProperty(ref _allNotesHidden, value))
            {
                OnPropertyChanged(nameof(ToggleAllButtonText));
            }
        }
    }

    public string ToggleAllButtonText => AllNotesHidden ? "Показать все" : "Скрыть все";
    public bool HasNotes => Notes.Count > 0;
    public bool HasNoNotes => Notes.Count == 0;

    public string DefaultColorHex
    {
        get => _defaultColorHex;
        set
        {
            if (SetProperty(ref _defaultColorHex, value))
            {
                _settingsService.Current.StickyNotes.DefaultColorHex = value;
                _settingsService.Save();
            }
        }
    }

    public double DefaultOpacityPercent
    {
        get => _defaultOpacityPercent;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), 35, 100);
            if (SetProperty(ref _defaultOpacityPercent, clamped))
            {
                _settingsService.Current.StickyNotes.DefaultOpacity = clamped / 100.0;
                _settingsService.Save();
                OnPropertyChanged(nameof(DefaultOpacityText));
            }
        }
    }

    public string DefaultOpacityText => $"{Math.Round(DefaultOpacityPercent)}%";

    public double DefaultFontSize
    {
        get => _defaultFontSize;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), 10, 28);
            if (SetProperty(ref _defaultFontSize, clamped))
            {
                _settingsService.Current.StickyNotes.DefaultFontSize = clamped;
                _settingsService.Save();
                OnPropertyChanged(nameof(DefaultFontSizeText));
            }
        }
    }

    public string DefaultFontSizeText => $"{Math.Round(DefaultFontSize)} px";

    public bool DefaultTopmost
    {
        get => _defaultTopmost;
        set
        {
            if (SetProperty(ref _defaultTopmost, value))
            {
                _settingsService.Current.StickyNotes.DefaultTopmost = value;
                _settingsService.Save();
            }
        }
    }

    public void UpdateCreateHotkey(KeyModifiers modifiers, Key key)
    {
        if (modifiers == KeyModifiers.None || key == Key.None) return;

        var settings = _settingsService.Current.StickyNotes;
        settings.CreateHotkeyModifiers = modifiers;
        settings.CreateHotkeyKey = key;
        _settingsService.Save();

        CreateHotkeyText = StickyNotesModule.FormatHotkey(modifiers, key);
        if (_module.IsEnabled)
        {
            _module.RegisterHotkeys();
        }
        UpdateHotkeyConflicts();
    }

    public void UpdateToggleVisibilityHotkey(KeyModifiers modifiers, Key key)
    {
        if (modifiers == KeyModifiers.None || key == Key.None) return;

        var settings = _settingsService.Current.StickyNotes;
        settings.ToggleVisibilityHotkeyModifiers = modifiers;
        settings.ToggleVisibilityHotkeyKey = key;
        _settingsService.Save();

        ToggleVisibilityHotkeyText = StickyNotesModule.FormatHotkey(modifiers, key);
        if (_module.IsEnabled)
        {
            _module.RegisterHotkeys();
        }
        UpdateHotkeyConflicts();
    }

    public void RefreshNotes()
    {
        var snapshot = _module.GetNotesSnapshot();
        Notes.Clear();
        foreach (var note in snapshot.OrderByDescending(n => n.UpdatedAtUtc))
        {
            Notes.Add(new StickyNoteListItemViewModel(note));
        }

        AllNotesHidden = _settingsService.Current.StickyNotes.AllNotesHidden;
        var visibleCount = snapshot.Count(n => n.IsVisible);
        var hiddenCount = snapshot.Count - visibleCount;

        SummaryText = snapshot.Count == 0
            ? "Пока нет сохранённых стикеров. Создайте первый стикер кнопкой справа или горячей клавишей."
            : (AllNotesHidden
                ? $"Всего стикеров: {snapshot.Count} • Все временно скрыты"
                : $"Всего стикеров: {snapshot.Count} • На экране: {visibleCount} • Скрытых: {hiddenCount}");

        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(HasNoNotes));
    }

    private void LoadFromSettings()
    {
        var s = _settingsService.Current.StickyNotes;
        CreateHotkeyText = StickyNotesModule.FormatHotkey(s.CreateHotkeyModifiers, s.CreateHotkeyKey);
        ToggleVisibilityHotkeyText = StickyNotesModule.FormatHotkey(s.ToggleVisibilityHotkeyModifiers, s.ToggleVisibilityHotkeyKey);
        _defaultColorHex = StickyNotePalette.Resolve(s.DefaultColorHex).Id;
        _defaultOpacityPercent = Math.Clamp(Math.Round(s.DefaultOpacity * 100), 35, 100);
        _defaultFontSize = Math.Clamp(Math.Round(s.DefaultFontSize), 10, 28);
        _defaultTopmost = s.DefaultTopmost;
        _allNotesHidden = s.AllNotesHidden;
    }

    private void UpdateHotkeyConflicts()
    {
        CreateConflictMessage = _module.CreateHotkeyError;
        ToggleConflictMessage = _module.ToggleVisibilityHotkeyError;
    }

    private void OnModuleNotesChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            RefreshNotes();
            return;
        }

        if (dispatcher.CheckAccess())
        {
            RefreshNotes();
        }
        else
        {
            dispatcher.BeginInvoke(RefreshNotes);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _module.HotkeyStatusChanged -= UpdateHotkeyConflicts;
        _module.NotesChanged -= OnModuleNotesChanged;
    }
}
