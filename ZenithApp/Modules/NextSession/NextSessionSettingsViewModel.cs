using System.Windows.Input;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Settings;
using ZenithApp.Modules.Screenshot;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.NextSession;

public sealed class NextSessionSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly NextSessionModule _module;
    private readonly ISettingsService _settingsService;

    private string _hotkeyText = string.Empty;
    private string? _conflictMessage;
    private bool _onlyAfterSystemReboot;
    private bool _triggerOnResumeFromSleep;
    private int _startupDelaySeconds;

    public NextSessionSettingsViewModel(NextSessionModule module, ISettingsService settingsService)
    {
        _module = module;
        _settingsService = settingsService;

        LoadFromSettings();

        _module.HotkeyStatusChanged += UpdateConflictMessage;
        _module.PlanStateChanged += UpdatePlanSummary;
        UpdateConflictMessage();

        OpenPlannerCommand = new RelayCommand(() => _module.OpenPlannerWindow());
        ClearPlanCommand = new RelayCommand(() => _module.ClearPlan());
        ExecuteNowCommand = new RelayCommand(async () => await _module.ExecutePendingPlanAsync(force: true));
        PreviewCountdownCommand = new RelayCommand(async () => await _module.PreviewCountdownAsync());
    }

    public ICommand OpenPlannerCommand { get; }
    public ICommand ClearPlanCommand { get; }
    public ICommand ExecuteNowCommand { get; }
    public ICommand PreviewCountdownCommand { get; }

    public string HotkeyText
    {
        get => _hotkeyText;
        private set => SetProperty(ref _hotkeyText, value);
    }

    public string? ConflictMessage
    {
        get => _conflictMessage;
        private set => SetProperty(ref _conflictMessage, value);
    }

    public bool HasConflict => !string.IsNullOrEmpty(ConflictMessage);

    public bool OnlyAfterSystemReboot
    {
        get => _onlyAfterSystemReboot;
        set
        {
            if (SetProperty(ref _onlyAfterSystemReboot, value))
            {
                _settingsService.Current.NextSession.OnlyAfterSystemReboot = value;
                _settingsService.Save();
            }
        }
    }

    public bool TriggerOnResumeFromSleep
    {
        get => _triggerOnResumeFromSleep;
        set
        {
            if (SetProperty(ref _triggerOnResumeFromSleep, value))
            {
                _settingsService.Current.NextSession.TriggerOnResumeFromSleep = value;
                _settingsService.Save();
            }
        }
    }

    public int StartupDelaySeconds
    {
        get => _startupDelaySeconds;
        set
        {
            var clamped = Math.Clamp(value, 0, 30);
            if (SetProperty(ref _startupDelaySeconds, clamped))
            {
                _settingsService.Current.NextSession.StartupDelaySeconds = clamped;
                _settingsService.Save();
            }
        }
    }

    public bool HasPendingPlan => _module.Settings.HasPendingPlan;

    public string PendingPlanSummary
    {
        get
        {
            var cfg = _module.Settings;
            if (!cfg.HasPendingPlan)
            {
                return "Активного плана на следующий сеанс нет.";
            }

            var progCount = cfg.PendingPrograms?.Count ?? 0;
            var msgPreview = string.IsNullOrWhiteSpace(cfg.PendingMessage)
                ? "без текстовой заметки"
                : $"заметка: «{(cfg.PendingMessage.Length > 60 ? cfg.PendingMessage[..60] + "..." : cfg.PendingMessage)}»";

            return $"Запланировано программ: {progCount} ({msgPreview}).";
        }
    }

    public void UpdateHotkey(KeyModifiers modifiers, Key key)
    {
        var cfg = _settingsService.Current.NextSession;
        cfg.HotkeyModifiers = modifiers;
        cfg.HotkeyKey = key;
        _settingsService.Save();

        HotkeyText = ScreenshotSettingsViewModel.FormatHotkey(modifiers, key);

        if (_module.IsEnabled)
        {
            _module.RegisterHotkey();
        }
    }

    private void LoadFromSettings()
    {
        var cfg = _settingsService.Current.NextSession;
        HotkeyText = ScreenshotSettingsViewModel.FormatHotkey(cfg.HotkeyModifiers, cfg.HotkeyKey);
        OnlyAfterSystemReboot = cfg.OnlyAfterSystemReboot;
        TriggerOnResumeFromSleep = cfg.TriggerOnResumeFromSleep;
        StartupDelaySeconds = cfg.StartupDelaySeconds;
    }

    private void UpdateConflictMessage()
    {
        ConflictMessage = _module.HotkeyError;
        OnPropertyChanged(nameof(HasConflict));
    }

    private void UpdatePlanSummary()
    {
        var cfg = _module.Settings;
        if (_startupDelaySeconds != cfg.StartupDelaySeconds)
        {
            _startupDelaySeconds = cfg.StartupDelaySeconds;
            OnPropertyChanged(nameof(StartupDelaySeconds));
        }

        if (_triggerOnResumeFromSleep != cfg.TriggerOnResumeFromSleep)
        {
            _triggerOnResumeFromSleep = cfg.TriggerOnResumeFromSleep;
            OnPropertyChanged(nameof(TriggerOnResumeFromSleep));
        }

        OnPropertyChanged(nameof(HasPendingPlan));
        OnPropertyChanged(nameof(PendingPlanSummary));
    }

    public void Dispose()
    {
        _module.HotkeyStatusChanged -= UpdateConflictMessage;
        _module.PlanStateChanged -= UpdatePlanSummary;
    }
}
