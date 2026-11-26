using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Settings;
using ZenithApp.Modules.Screenshot;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.NextSession;

public sealed class NextSessionSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly NextSessionModule _module;
    private readonly ISettingsService _settingsService;
    private readonly IRunningAppsTerminationService _terminationService;
    private readonly HashSet<string> _terminatingKeys = new(StringComparer.OrdinalIgnoreCase);

    private string _hotkeyText = string.Empty;
    private string? _conflictMessage;
    private int _startupDelaySeconds;
    private bool _isTerminatingAll;
    private bool _isRefreshingApps;
    private bool _disposed;

    public NextSessionSettingsViewModel(
        NextSessionModule module,
        ISettingsService settingsService,
        IRunningAppsTerminationService? terminationService = null)
    {
        _module = module;
        _settingsService = settingsService;
        _terminationService = terminationService ?? new RunningAppsTerminationService(new FileLogger());

        RunningApps = new ObservableCollection<RunningAppItem>();

        LoadFromSettings();

        _module.HotkeyStatusChanged += UpdateConflictMessage;
        _module.PlanStateChanged += UpdatePlanSummary;
        UpdateConflictMessage();

        OpenPlannerCommand = new RelayCommand(() => _module.OpenPlannerWindow());
        ClearPlanCommand = new RelayCommand(() => _module.ClearPlan());
        ExecuteNowCommand = new RelayCommand(async () => await _module.ExecutePendingPlanAsync(force: true));
        PreviewCountdownCommand = new RelayCommand(async () => await _module.PreviewCountdownAsync());
        RefreshRunningAppsCommand = new RelayCommand(async () => await RefreshRunningAppsAsync());
        TerminateAppCommand = new RelayCommand(async param =>
        {
            if (param is RunningAppItem item)
            {
                await TerminateAppAsync(item);
            }
        });
        TerminateAllAppsCommand = new RelayCommand(
            async () => await TerminateAllAppsAsync(),
            () => CanTerminateAll);
    }

    public ICommand OpenPlannerCommand { get; }
    public ICommand ClearPlanCommand { get; }
    public ICommand ExecuteNowCommand { get; }
    public ICommand PreviewCountdownCommand { get; }
    public ICommand RefreshRunningAppsCommand { get; }
    public ICommand TerminateAppCommand { get; }
    public ICommand TerminateAllAppsCommand { get; }

    public ObservableCollection<RunningAppItem> RunningApps { get; }

    public int RunningAppsCount => RunningApps.Count;
    public bool HasRunningApps => RunningApps.Count > 0;
    public bool HasNoRunningApps => RunningApps.Count == 0;

    public bool IsTerminatingAll
    {
        get => _isTerminatingAll;
        private set
        {
            if (SetProperty(ref _isTerminatingAll, value))
            {
                OnPropertyChanged(nameof(CanTerminateAll));
            }
        }
    }

    public bool CanTerminateAll => !IsTerminatingAll && RunningApps.Any(a => !a.IsTerminating);

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

    public async Task RefreshRunningAppsAsync()
    {
        if (_disposed || _isRefreshingApps) return;
        _isRefreshingApps = true;

        try
        {
            var fresh = await Task.Run(() => _terminationService.GetAltTabRunningApps());
            if (_disposed) return;

            ApplyRunningAppsSnapshot(fresh);
        }
        finally
        {
            _isRefreshingApps = false;
        }
    }

    public async Task TerminateAppAsync(RunningAppItem? app)
    {
        if (_disposed || app == null || app.IsTerminating || _terminatingKeys.Contains(app.Key))
        {
            return;
        }

        _terminatingKeys.Add(app.Key);
        app.IsTerminating = true;
        OnPropertyChanged(nameof(CanTerminateAll));

        TerminationResult result;
        try
        {
            result = await _terminationService.TerminateAppAsync(app);
        }
        finally
        {
            _terminatingKeys.Remove(app.Key);
        }

        if (_disposed) return;

        if (result.Success)
        {
            RunningApps.Remove(app);
            NotifyRunningAppsChanged();
            await RefreshRunningAppsAsync();
        }
        else
        {
            app.IsTerminating = false;
            OnPropertyChanged(nameof(CanTerminateAll));

            if ((result.AccessDenied || !string.IsNullOrWhiteSpace(result.ErrorMessage)) && Application.Current != null)
            {
                NotificationToast.Show(
                    result.ErrorMessage ?? $"Не удалось завершить «{app.DisplayName}».",
                    "Завершение",
                    ToastNotificationType.Error,
                    durationSeconds: 4);
            }

            await RefreshRunningAppsAsync();
        }
    }

    public async Task TerminateAllAppsAsync()
    {
        if (_disposed || IsTerminatingAll) return;

        var targets = RunningApps.Where(a => !a.IsTerminating && !_terminatingKeys.Contains(a.Key)).ToList();
        if (targets.Count == 0) return;

        IsTerminatingAll = true;
        foreach (var app in targets)
        {
            _terminatingKeys.Add(app.Key);
            app.IsTerminating = true;
        }
        OnPropertyChanged(nameof(CanTerminateAll));

        try
        {
            var results = await Task.WhenAll(targets.Select(async app =>
            {
                var res = await _terminationService.TerminateAppAsync(app);
                return (App: app, Result: res);
            }));

            if (_disposed) return;

            var failedNames = new List<string>();
            foreach (var (app, res) in results)
            {
                _terminatingKeys.Remove(app.Key);
                if (res.Success)
                {
                    RunningApps.Remove(app);
                }
                else
                {
                    app.IsTerminating = false;
                    if (res.AccessDenied)
                    {
                        failedNames.Add(app.DisplayName);
                    }
                }
            }

            NotifyRunningAppsChanged();

            if (failedNames.Count > 0 && Application.Current != null)
            {
                NotificationToast.Show(
                    $"Не удалось завершить (требуются права администратора): {string.Join(", ", failedNames)}.",
                    "Завершение",
                    ToastNotificationType.Error,
                    durationSeconds: 5);
            }
        }
        finally
        {
            foreach (var app in targets)
            {
                _terminatingKeys.Remove(app.Key);
            }

            if (!_disposed)
            {
                IsTerminatingAll = false;
                await RefreshRunningAppsAsync();
            }
        }
    }

    private void ApplyRunningAppsSnapshot(IReadOnlyList<RunningAppItem> fresh)
    {
        if (AreSnapshotsEqual(RunningApps, fresh))
        {
            return;
        }

        RunningApps.Clear();
        foreach (var item in fresh)
        {
            if (_terminatingKeys.Contains(item.Key))
            {
                item.IsTerminating = true;
            }
            RunningApps.Add(item);
        }

        NotifyRunningAppsChanged();
    }

    private static bool AreSnapshotsEqual(IReadOnlyList<RunningAppItem> current, IReadOnlyList<RunningAppItem> fresh)
    {
        if (current.Count != fresh.Count) return false;

        for (int i = 0; i < current.Count; i++)
        {
            var a = current[i];
            var b = fresh[i];

            if (!string.Equals(a.Key, b.Key, StringComparison.OrdinalIgnoreCase) ||
                !a.WindowHandles.SequenceEqual(b.WindowHandles) ||
                !a.ProcessIds.SequenceEqual(b.ProcessIds))
            {
                return false;
            }
        }

        return true;
    }

    private void NotifyRunningAppsChanged()
    {
        OnPropertyChanged(nameof(RunningAppsCount));
        OnPropertyChanged(nameof(HasRunningApps));
        OnPropertyChanged(nameof(HasNoRunningApps));
        OnPropertyChanged(nameof(CanTerminateAll));
        CommandManager.InvalidateRequerySuggested();
    }

    private void LoadFromSettings()
    {
        var cfg = _settingsService.Current.NextSession;
        HotkeyText = ScreenshotSettingsViewModel.FormatHotkey(cfg.HotkeyModifiers, cfg.HotkeyKey);
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

        OnPropertyChanged(nameof(HasPendingPlan));
        OnPropertyChanged(nameof(PendingPlanSummary));
    }

    public void Dispose()
    {
        _disposed = true;
        _module.HotkeyStatusChanged -= UpdateConflictMessage;
        _module.PlanStateChanged -= UpdatePlanSummary;
    }
}
