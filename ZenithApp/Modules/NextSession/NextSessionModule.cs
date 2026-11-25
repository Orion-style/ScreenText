using System.Windows;
using Microsoft.Win32;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;

namespace ZenithApp.Modules.NextSession;

public sealed class NextSessionModule : BaseFeatureModule
{
    public const string ModuleId = "next_session";
    public const string HotkeyId = "next_session_open";

    private readonly ISettingsService _settingsService;
    private readonly IHotkeyService _hotkeyService;
    private readonly INextSessionProgramCatalogService _catalogService;
    private readonly INextSessionLauncherService _launcherService;
    private readonly ISystemBootTimeProvider _bootTimeProvider;

    private readonly object _windowLock = new();
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private NextSessionWindow? _plannerWindow;
    private CancellationTokenSource? _startupExecutionCts;
    private bool _systemEventsSubscribed;

    public NextSessionModule(
        IAppLogger logger,
        ISettingsService settingsService,
        IHotkeyService hotkeyService,
        INextSessionProgramCatalogService catalogService,
        INextSessionLauncherService launcherService,
        ISystemBootTimeProvider? bootTimeProvider = null) : base(logger)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _catalogService = catalogService;
        _launcherService = launcherService;
        _bootTimeProvider = bootTimeProvider ?? new SystemBootTimeProvider();
    }

    public override string Id => ModuleId;
    public override string Name => "Следующий сеанс";
    public override string Description => "Настройка программ для запуска и заметки-уведомления на следующий запуск после перезагрузки.";

    public NextSessionSettings Settings => _settingsService.Current.NextSession;
    public string? HotkeyError { get; private set; }

    public event Action? HotkeyStatusChanged;
    public event Action? PlanStateChanged;

    protected override Task OnEnableAsync()
    {
        RegisterHotkey();
        SubscribeSystemEvents();

        if (ShouldExecutePendingPlanOnStartup())
        {
            _startupExecutionCts?.Cancel();
            _startupExecutionCts?.Dispose();
            _startupExecutionCts = new CancellationTokenSource();
            var token = _startupExecutionCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecutePendingPlanAsync(force: false, token);
                }
                catch (OperationCanceledException)
                {
                    // Cancelled during module disable/shutdown
                }
                catch (Exception ex)
                {
                    Logger.LogError("[NextSession] Unexpected error executing startup plan.", ex);
                }
            }, token);
        }

        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        _startupExecutionCts?.Cancel();
        _startupExecutionCts?.Dispose();
        _startupExecutionCts = null;

        UnsubscribeSystemEvents();
        UnregisterHotkey();
        ClosePlannerWindow();

        return Task.CompletedTask;
    }

    public void RegisterHotkey()
    {
        UnregisterHotkey();

        var config = Settings;
        var success = _hotkeyService.RegisterHotkey(
            HotkeyId,
            config.HotkeyModifiers,
            config.HotkeyKey,
            () =>
            {
                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.Invoke(OpenPlannerWindow);
                }
                else
                {
                    OpenPlannerWindow();
                }
            });

        HotkeyError = success
            ? null
            : $"Горячая клавиша {config.HotkeyModifiers} + {config.HotkeyKey} уже занята другим приложением.";

        HotkeyStatusChanged?.Invoke();
    }

    public void UnregisterHotkey()
    {
        _hotkeyService.UnregisterHotkey(HotkeyId);
        HotkeyError = null;
        HotkeyStatusChanged?.Invoke();
    }

    public bool ShouldExecutePendingPlanOnStartup()
    {
        var config = Settings;
        if (!config.HasPendingPlan) return false;

        if (string.IsNullOrWhiteSpace(config.PendingMessage) && config.PendingPrograms.Count == 0)
        {
            return false;
        }

        if (!config.OnlyAfterSystemReboot)
        {
            return true;
        }

        if (config.SystemShutdownObserved)
        {
            return true;
        }

        if (!config.PlannedBootTimestampUtc.HasValue)
        {
            return true;
        }

        var currentBoot = _bootTimeProvider.GetCurrentBootTimestampUtc();
        return Math.Abs((currentBoot - config.PlannedBootTimestampUtc.Value).TotalSeconds) > 60.0;
    }

    public async Task<bool> ExecutePendingPlanAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!await _executionGate.WaitAsync(0, cancellationToken))
        {
            return false;
        }

        try
        {
            var config = Settings;
            if (!config.HasPendingPlan) return false;
            if (string.IsNullOrWhiteSpace(config.PendingMessage) && config.PendingPrograms.Count == 0)
            {
                return false;
            }

            var messageSnapshot = config.PendingMessage;
            var programsSnapshot = config.PendingPrograms.ToList();
            var delaySeconds = Math.Clamp(config.StartupDelaySeconds, 0, 30);
            var intervalMs = Math.Clamp(config.LaunchIntervalMs, 50, 5000);

            if (!force && delaySeconds > 0)
            {
                var outcome = await _launcherService.WaitWithCountdownAsync(
                    delaySeconds,
                    programsSnapshot.Count,
                    messageSnapshot,
                    cancellationToken);

                if (outcome == NextSessionCountdownOutcome.Cancelled)
                {
                    Logger.LogInfo("[NextSession] Startup launch cancelled by user from countdown overlay.");
                    config.HasPendingPlan = false;
                    config.PendingMessage = string.Empty;
                    config.PendingPrograms = new List<NextSessionProgramEntry>();
                    config.PlannedBootTimestampUtc = null;
                    config.SystemShutdownObserved = false;
                    _settingsService.Save();
                    NotifyPlanStateChanged();
                    return false;
                }
            }

            int launchedCount = 0;
            var failedNames = new List<string>();

            for (int i = 0; i < programsSnapshot.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = programsSnapshot[i];
                if (_launcherService.LaunchProgram(entry))
                {
                    launchedCount++;
                }
                else
                {
                    failedNames.Add(string.IsNullOrWhiteSpace(entry.Name) ? entry.ExecutablePath : entry.Name);
                }

                if (i < programsSnapshot.Count - 1 && intervalMs > 0)
                {
                    await Task.Delay(intervalMs, cancellationToken);
                }
            }

            _launcherService.ShowSessionNotification(messageSnapshot, launchedCount, failedNames);

            config.HasPendingPlan = false;
            config.PendingMessage = string.Empty;
            config.PendingPrograms = new List<NextSessionProgramEntry>();
            config.PlannedBootTimestampUtc = null;
            config.SystemShutdownObserved = false;
            config.LastExecutedAtUtc = DateTime.UtcNow;
            _settingsService.Save();

            NotifyPlanStateChanged();
            return true;
        }
        finally
        {
            _executionGate.Release();
        }
    }

    public void SavePlan(
        string message,
        IReadOnlyList<NextSessionProgramEntry> selectedPrograms,
        IReadOnlyList<NextSessionProgramEntry>? customPrograms = null)
    {
        var config = Settings;
        var trimmedMessage = message?.Trim() ?? string.Empty;
        var programsList = selectedPrograms?.ToList() ?? new List<NextSessionProgramEntry>();

        if (customPrograms != null)
        {
            config.CustomPrograms = customPrograms.ToList();
        }

        var hasContent = !string.IsNullOrWhiteSpace(trimmedMessage) || programsList.Count > 0;
        config.HasPendingPlan = hasContent;
        config.PendingMessage = trimmedMessage;
        config.PendingPrograms = programsList;
        config.PlannedBootTimestampUtc = hasContent ? _bootTimeProvider.GetCurrentBootTimestampUtc() : null;
        config.PlannedAtUtc = hasContent ? DateTime.UtcNow : null;
        config.SystemShutdownObserved = false;

        _settingsService.Save();
        NotifyPlanStateChanged();
    }

    public void ClearPlan()
    {
        var config = Settings;
        config.HasPendingPlan = false;
        config.PendingMessage = string.Empty;
        config.PendingPrograms = new List<NextSessionProgramEntry>();
        config.PlannedBootTimestampUtc = null;
        config.PlannedAtUtc = null;
        config.SystemShutdownObserved = false;

        _settingsService.Save();
        NotifyPlanStateChanged();
    }

    public bool SaveAndRestart(
        string message,
        IReadOnlyList<NextSessionProgramEntry> selectedPrograms,
        IReadOnlyList<NextSessionProgramEntry>? customPrograms = null)
    {
        SavePlan(message, selectedPrograms, customPrograms);
        return _launcherService.RequestSystemRestart();
    }

    public void OpenPlannerWindow()
    {
        if (Application.Current == null) return;

        try
        {
            lock (_windowLock)
            {
                if (_plannerWindow != null)
                {
                    if (_plannerWindow.WindowState == WindowState.Minimized)
                    {
                        _plannerWindow.WindowState = WindowState.Normal;
                    }
                    _plannerWindow.Activate();
                    _plannerWindow.Focus();
                    return;
                }

                var vm = new NextSessionWindowViewModel(this, _catalogService);
                _plannerWindow = new NextSessionWindow(vm);
                _plannerWindow.Closed += (_, _) =>
                {
                    vm.Dispose();
                    lock (_windowLock)
                    {
                        _plannerWindow = null;
                    }
                };

                _plannerWindow.Show();
                _plannerWindow.Activate();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("[NextSession] Failed to open planner window.", ex);
            ZenithApp.Modules.Screenshot.NotificationToast.Show(
                $"Не удалось открыть окно следующего сеанса: {ex.Message}",
                "Ошибка",
                ZenithApp.Modules.Screenshot.ToastNotificationType.Error);
        }
    }

    private void ClosePlannerWindow()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        dispatcher.Invoke(() =>
        {
            lock (_windowLock)
            {
                _plannerWindow?.Close();
                _plannerWindow = null;
            }
        });
    }

    private void SubscribeSystemEvents()
    {
        if (_systemEventsSubscribed) return;

        try
        {
            SystemEvents.SessionEnding += OnSessionEnding;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            _systemEventsSubscribed = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[NextSession] Could not subscribe to Windows system events: {ex.Message}");
        }
    }

    private void UnsubscribeSystemEvents()
    {
        if (!_systemEventsSubscribed) return;

        try
        {
            SystemEvents.SessionEnding -= OnSessionEnding;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        catch
        {
            // Ignore cleanup errors
        }
        finally
        {
            _systemEventsSubscribed = false;
        }
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        if (Settings.HasPendingPlan)
        {
            Settings.SystemShutdownObserved = true;
            _settingsService.Save();
            Logger.LogInfo($"[NextSession] Observed Windows session ending ({e.Reason}); plan armed for next startup.");
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume && Settings.TriggerOnResumeFromSleep && Settings.HasPendingPlan)
        {
            Logger.LogInfo("[NextSession] System resumed from sleep with active plan; executing.");
            _ = Task.Run(() => ExecutePendingPlanAsync(force: false));
        }
    }

    private void NotifyPlanStateChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => PlanStateChanged?.Invoke());
        }
        else
        {
            PlanStateChanged?.Invoke();
        }
    }
}
