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
            Logger.LogInfo("[NextSession] Pending plan armed (OnlyAfterSystemReboot=false); executing.");
            return true;
        }

        if (config.SystemShutdownObserved)
        {
            Logger.LogInfo("[NextSession] Pending plan armed (SystemShutdownObserved=true); executing.");
            return true;
        }

        // Fast Startup (Hiberboot) and Hibernate do not reset TickCount64, so check Windows logon session & BootId first
        var currentLogonSessionId = _bootTimeProvider.GetCurrentLogonSessionId();
        if (!string.IsNullOrWhiteSpace(config.PlannedLogonSessionId) &&
            !string.IsNullOrWhiteSpace(currentLogonSessionId) &&
            !string.Equals(currentLogonSessionId, config.PlannedLogonSessionId, StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInfo($"[NextSession] New Windows logon session detected ({config.PlannedLogonSessionId} -> {currentLogonSessionId}); executing pending plan.");
            return true;
        }

        var currentKernelBootId = _bootTimeProvider.GetCurrentKernelBootId();
        if (config.PlannedKernelBootId.HasValue &&
            currentKernelBootId.HasValue &&
            currentKernelBootId.Value != config.PlannedKernelBootId.Value)
        {
            Logger.LogInfo($"[NextSession] Kernel BootId changed ({config.PlannedKernelBootId.Value} -> {currentKernelBootId.Value}); executing pending plan.");
            return true;
        }

        // Backward-compatible check for plans saved before PlannedLogonSessionId existed
        var currentLogonTimeUtc = _bootTimeProvider.GetCurrentLogonTimestampUtc();
        if (currentLogonTimeUtc.HasValue &&
            config.PlannedAtUtc.HasValue &&
            currentLogonTimeUtc.Value > config.PlannedAtUtc.Value.AddSeconds(2))
        {
            Logger.LogInfo($"[NextSession] Windows session logon ({currentLogonTimeUtc.Value:O}) occurred after plan save ({config.PlannedAtUtc.Value:O}); executing pending plan.");
            return true;
        }

        if (!config.PlannedBootTimestampUtc.HasValue)
        {
            Logger.LogInfo("[NextSession] PlannedBootTimestampUtc missing; executing pending plan.");
            return true;
        }

        var currentBoot = _bootTimeProvider.GetCurrentBootTimestampUtc();
        if (Math.Abs((currentBoot - config.PlannedBootTimestampUtc.Value).TotalSeconds) > 60.0)
        {
            Logger.LogInfo($"[NextSession] System boot timestamp changed ({config.PlannedBootTimestampUtc.Value:O} -> {currentBoot:O}); executing pending plan.");
            return true;
        }

        Logger.LogInfo("[NextSession] Pending plan kept waiting: same Windows session and boot detected.");
        return false;
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
            var programNames = programsSnapshot
                .Select(p => !string.IsNullOrWhiteSpace(p.Name) ? p.Name.Trim() : p.ExecutablePath)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();
            var delaySeconds = Math.Clamp(config.StartupDelaySeconds, 0, 30);
            var intervalMs = Math.Clamp(config.LaunchIntervalMs, 50, 5000);

            if (!force && delaySeconds > 0)
            {
                var outcome = await _launcherService.WaitWithCountdownAsync(
                    delaySeconds,
                    programNames,
                    messageSnapshot,
                    cancellationToken);

                if (outcome == NextSessionCountdownOutcome.Cancelled)
                {
                    Logger.LogInfo("[NextSession] Startup launch cancelled by user from countdown overlay.");
                    ResetPendingPlanState(config);
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

            ResetPendingPlanState(config);
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

    public async Task<NextSessionCountdownOutcome> PreviewCountdownAsync(CancellationToken cancellationToken = default)
    {
        var config = Settings;
        var delaySeconds = Math.Clamp(config.StartupDelaySeconds, 1, 30);
        var programNames = config.PendingPrograms
            .Select(p => !string.IsNullOrWhiteSpace(p.Name) ? p.Name.Trim() : p.ExecutablePath)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        if (programNames.Count == 0 && string.IsNullOrWhiteSpace(config.PendingMessage))
        {
            programNames.Add("Пример приложения");
        }

        var message = string.IsNullOrWhiteSpace(config.PendingMessage)
            ? "Предпросмотр таймера запуска"
            : config.PendingMessage;

        return await _launcherService.WaitWithCountdownAsync(
            delaySeconds,
            programNames,
            message,
            cancellationToken);
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
        config.PlannedLogonSessionId = hasContent ? _bootTimeProvider.GetCurrentLogonSessionId() : null;
        config.PlannedKernelBootId = hasContent ? _bootTimeProvider.GetCurrentKernelBootId() : null;
        config.PlannedAtUtc = hasContent ? DateTime.UtcNow : null;
        config.SystemShutdownObserved = false;

        _settingsService.Save();
        NotifyPlanStateChanged();
    }

    public void ClearPlan()
    {
        ResetPendingPlanState(Settings);
        _settingsService.Save();
        NotifyPlanStateChanged();
    }

    public void UpdateStartupDelaySeconds(int delaySeconds)
    {
        var clamped = Math.Clamp(delaySeconds, 0, 30);
        if (Settings.StartupDelaySeconds == clamped) return;

        Settings.StartupDelaySeconds = clamped;
        _settingsService.Save();
        NotifyPlanStateChanged();
    }

    public void UpdateTriggerOnResumeFromSleep(bool triggerOnResume)
    {
        if (Settings.TriggerOnResumeFromSleep == triggerOnResume) return;

        Settings.TriggerOnResumeFromSleep = triggerOnResume;
        _settingsService.Save();
        NotifyPlanStateChanged();
    }

    private static void ResetPendingPlanState(NextSessionSettings config)
    {
        config.HasPendingPlan = false;
        config.PendingMessage = string.Empty;
        config.PendingPrograms = new List<NextSessionProgramEntry>();
        config.PlannedBootTimestampUtc = null;
        config.PlannedLogonSessionId = null;
        config.PlannedKernelBootId = null;
        config.PlannedAtUtc = null;
        config.SystemShutdownObserved = false;
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
