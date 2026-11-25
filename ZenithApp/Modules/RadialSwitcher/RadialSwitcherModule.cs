using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using ZenithApp.Core.Input;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;
using ZenithApp.Modules.KeyRemap;

namespace ZenithApp.Modules.RadialSwitcher;

public sealed class RadialSwitcherModule : BaseFeatureModule
{
    public const string ModuleId = "radial_switcher";

    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    internal static Func<int, bool>? ModifierChecker { get; set; }

    private static bool IsModifierKeyDown(int vKey)
    {
        if (ModifierChecker != null)
        {
            return ModifierChecker(vKey);
        }

        return (GetAsyncKeyState(vKey) & 0x8000) != 0;
    }

    private readonly ISettingsService _settingsService;
    private readonly IKeyboardHookService _keyboardHookService;

    private IDisposable? _hookSubscription;
    private System.Threading.Timer? _holdTimer;
    private RadialWheelWindow? _currentWheel;

    private bool _isTabHeld;
    private bool _isWheelOpen;
    private bool _wheelOpenedInCurrentHold;
    private bool _holdCancelledForCurrentHold;
    private bool _tabPassedThroughForCurrentHold;
    private long _holdGeneration;
    private readonly object _stateLock = new();

    internal Action<int>? SendKeyPressAction { get; set; }
    internal Action<int>? SendKeyDownAction { get; set; }
    internal Action? WheelOpenRequestedAction { get; set; }
    internal Action? WheelCloseAndActivateAction { get; set; }
    internal Action? WheelCloseImmediatelyAction { get; set; }
    internal bool IsWheelOpenForTest { get { lock (_stateLock) return _isWheelOpen; } }

    public RadialSwitcherSettings Settings => _settingsService.Current.RadialSwitcher;

    public RadialSwitcherModule(
        IAppLogger logger,
        ISettingsService settingsService,
        IKeyboardHookService keyboardHookService) : base(logger)
    {
        _settingsService = settingsService;
        _keyboardHookService = keyboardHookService;
    }

    public override string Id => ModuleId;
    public override string Name => "Перемещение и переключение окон (Колесо Tab)";
    public override string Description => "Круговое радиальное меню быстрого переключения и перемещения между окнами при зажатии клавиши Tab в стиле селектора Genshin Impact с настройкой секторов и зон.";

    protected override Task OnEnableAsync()
    {
        _hookSubscription = _keyboardHookService.Subscribe(OnKeyboardInput);

        Application.Current?.Dispatcher.BeginInvoke(WindowListService.StartTracking);

        Logger.LogInfo("[RadialSwitcher] Модуль активирован. Перехват зажатия Tab и отслеживание MRU активности окон включены.");
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        _hookSubscription?.Dispose();
        _hookSubscription = null;

        StopHoldTimer();

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            WindowListService.StopTracking();
            _currentWheel?.Close();
            _currentWheel = null;
        });

        lock (_stateLock)
        {
            _isTabHeld = false;
            _isWheelOpen = false;
            _wheelOpenedInCurrentHold = false;
            _holdCancelledForCurrentHold = false;
            _tabPassedThroughForCurrentHold = false;
            _holdGeneration++;
        }

        Logger.LogInfo("[RadialSwitcher] Модуль отключен.");
        return Task.CompletedTask;
    }

    private void StartHoldTimer(int delayMs)
    {
        lock (_stateLock)
        {
            _holdTimer?.Dispose();
            var generation = ++_holdGeneration;
            _holdTimer = new System.Threading.Timer(OnHoldTimerCallback, generation, delayMs, Timeout.Infinite);
        }
    }

    private void StopHoldTimer()
    {
        lock (_stateLock)
        {
            _holdGeneration++;
            _holdTimer?.Dispose();
            _holdTimer = null;
        }
    }

    private void OnHoldTimerCallback(object? state)
    {
        if (state is not long expectedGeneration)
        {
            return;
        }

        lock (_stateLock)
        {
            if (!_isTabHeld ||
                _tabPassedThroughForCurrentHold ||
                _holdCancelledForCurrentHold ||
                expectedGeneration != _holdGeneration)
            {
                return;
            }

            _isWheelOpen = true;
            _wheelOpenedInCurrentHold = true;
        }

        if (WheelOpenRequestedAction != null)
        {
            if (IsGenerationStillValidForWheel(expectedGeneration))
            {
                WheelOpenRequestedAction.Invoke();
            }
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(() => OpenWheelWindow(expectedGeneration));
    }

    private bool IsGenerationStillValidForWheel(long expectedGeneration)
    {
        lock (_stateLock)
        {
            return _isTabHeld &&
                   _isWheelOpen &&
                   !_tabPassedThroughForCurrentHold &&
                   !_holdCancelledForCurrentHold &&
                   expectedGeneration == _holdGeneration;
        }
    }

    private void EmitTabKeyPress()
    {
        if (SendKeyPressAction != null)
        {
            SendKeyPressAction(VK_TAB);
            return;
        }

        KeyboardInputSender.SendKeyPress(VK_TAB);
    }

    private void EmitTabKeyDown()
    {
        if (SendKeyDownAction != null)
        {
            SendKeyDownAction(VK_TAB);
            return;
        }

        KeyboardInputSender.SendKeyDown(VK_TAB);
    }

    private void RequestCloseWheelImmediately()
    {
        if (WheelCloseImmediatelyAction != null)
        {
            WheelCloseImmediatelyAction.Invoke();
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _currentWheel?.CloseImmediately();
            _currentWheel = null;
        });
    }

    private void RequestCloseWheelAndActivate()
    {
        if (WheelCloseAndActivateAction != null)
        {
            WheelCloseAndActivateAction.Invoke();
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_currentWheel != null)
            {
                _currentWheel.CloseAndActivateSelected();
                _currentWheel = null;
            }
        });
    }

    internal bool OnKeyboardInput(KeyboardHookEventArgs e)
    {
        // Игнорируем синтетические нажатия, сгенерированные нашим приложением
        if (e.IsInjected || e.ExtraInfo == KeyboardHookService.ExtraInfoAppMarker)
        {
            return false;
        }

        // Если нажат Escape при открытом меню или зажатом Tab — мгновенная отмена и закрытие
        if (e.VirtualCode == VK_ESCAPE)
        {
            if (e.IsKeyDown)
            {
                bool shouldCancel = false;
                lock (_stateLock)
                {
                    if ((_isTabHeld && !_tabPassedThroughForCurrentHold) || _isWheelOpen)
                    {
                        shouldCancel = true;
                        _isWheelOpen = false;
                        _holdCancelledForCurrentHold = _isTabHeld;
                        _holdGeneration++;
                        _holdTimer?.Dispose();
                        _holdTimer = null;
                    }
                }

                if (shouldCancel)
                {
                    RequestCloseWheelImmediately();
                    e.SuppressKeyPress = true;
                    return true;
                }
            }

            return false;
        }

        // Если нажата клавиша Windows (LWIN/RWIN) при зажатом Tab или открытом колесе:
        // мгновенно отменяем колесо, чтобы оно не застревало при вызове Пуск или смене столов
        if (e.VirtualCode == VK_LWIN || e.VirtualCode == VK_RWIN)
        {
            if (e.IsKeyDown)
            {
                bool wasActive = false;
                lock (_stateLock)
                {
                    if ((_isTabHeld && !_tabPassedThroughForCurrentHold) || _isWheelOpen)
                    {
                        wasActive = true;
                        _isWheelOpen = false;
                        _holdCancelledForCurrentHold = _isTabHeld;
                        _holdGeneration++;
                        _holdTimer?.Dispose();
                        _holdTimer = null;
                    }
                }

                if (wasActive)
                {
                    RequestCloseWheelImmediately();
                }
            }

            return false;
        }

        if (e.VirtualCode != VK_TAB)
        {
            // Быстрый rollover при наборе текста: если нажата другая обычная клавиша,
            // пока Tab был кратковременно зажат (колесо ещё не открыто), немедленно
            // пробрасываем Tab в нужном порядке и отменяем ожидание колеса.
            if (e.IsKeyDown && !VirtualKeyHelper.IsModifierKey(e.VirtualCode))
            {
                bool shouldFlushTabDown = false;
                lock (_stateLock)
                {
                    if (_isTabHeld &&
                        !_isWheelOpen &&
                        !_wheelOpenedInCurrentHold &&
                        !_holdCancelledForCurrentHold &&
                        !_tabPassedThroughForCurrentHold)
                    {
                        _tabPassedThroughForCurrentHold = true;
                        _holdGeneration++;
                        _holdTimer?.Dispose();
                        _holdTimer = null;
                        shouldFlushTabDown = true;
                    }
                }

                if (shouldFlushTabDown)
                {
                    EmitTabKeyDown();
                }
            }

            return false;
        }

        // Если зажат Alt, Ctrl, Shift или Win — это системная комбинация:
        // Alt+Tab (переключение окон Windows), Ctrl+Tab (переключение вкладок),
        // Shift+Tab (обратный Tab), Win+Tab (Task View).
        // Пропускаем мгновенно без перехвата и без задержки, закрывая радиальное меню если оно было открыто!
        if (IsModifierKeyDown(VK_MENU) ||
            IsModifierKeyDown(VK_CONTROL) ||
            IsModifierKeyDown(VK_SHIFT) ||
            IsModifierKeyDown(VK_LWIN) ||
            IsModifierKeyDown(VK_RWIN))
        {
            bool wasOpen = false;
            lock (_stateLock)
            {
                if (_isTabHeld || _isWheelOpen)
                {
                    wasOpen = _isWheelOpen;
                    _isTabHeld = false;
                    _isWheelOpen = false;
                    _wheelOpenedInCurrentHold = false;
                    _holdCancelledForCurrentHold = false;
                    _tabPassedThroughForCurrentHold = false;
                    _holdGeneration++;
                    _holdTimer?.Dispose();
                    _holdTimer = null;
                }
            }

            if (wasOpen)
            {
                RequestCloseWheelImmediately();
            }

            return false;
        }

        if (e.IsKeyDown)
        {
            lock (_stateLock)
            {
                if (!_isTabHeld)
                {
                    _isTabHeld = true;
                    _isWheelOpen = false;
                    _wheelOpenedInCurrentHold = false;
                    _holdCancelledForCurrentHold = false;
                    _tabPassedThroughForCurrentHold = false;

                    int delay = Math.Max(120, Settings.HoldDelayMs);
                    StartHoldTimer(delay);

                    // Временно подавляем Tab до решения (клик или удержание)
                    e.SuppressKeyPress = true;
                    return true;
                }

                if (_tabPassedThroughForCurrentHold)
                {
                    return false;
                }

                // Повторные сообщения автоповтора клавиши Windows при удержании Tab
                e.SuppressKeyPress = true;
                return true;
            }
        }
        else if (e.IsKeyUp)
        {
            bool wasWheelOpen;
            bool wasTabHeld;
            bool wasWheelOpenedInCurrentHold;
            bool wasCancelled;
            bool wasPassedThrough;

            lock (_stateLock)
            {
                wasWheelOpen = _isWheelOpen;
                wasTabHeld = _isTabHeld;
                wasWheelOpenedInCurrentHold = _wheelOpenedInCurrentHold;
                wasCancelled = _holdCancelledForCurrentHold;
                wasPassedThrough = _tabPassedThroughForCurrentHold;

                _isTabHeld = false;
                _isWheelOpen = false;
                _wheelOpenedInCurrentHold = false;
                _holdCancelledForCurrentHold = false;
                _tabPassedThroughForCurrentHold = false;
                StopHoldTimer();
            }

            if (wasWheelOpen)
            {
                // Пользователь отпустил Tab после удержания: активируем выбранную зону и закрываем колесо
                RequestCloseWheelAndActivate();

                e.SuppressKeyPress = true;
                return true;
            }

            if (wasPassedThrough)
            {
                return false;
            }

            if (wasTabHeld && !wasWheelOpenedInCurrentHold && !wasCancelled)
            {
                // Был короткий одиночный клик по Tab
                // Отправляем штатный атомарный системный Tab (Down + Up) в активное приложение
                EmitTabKeyPress();

                e.SuppressKeyPress = true;
                return true;
            }

            if (wasTabHeld)
            {
                e.SuppressKeyPress = true;
                return true;
            }
        }

        return false;
    }

    private void OpenWheelWindow(long expectedGeneration)
    {
        try
        {
            if (!IsGenerationStillValidForWheel(expectedGeneration))
            {
                return;
            }

            var fgHwnd = WindowListService.GetForegroundWindow();
            if (fgHwnd != nint.Zero)
            {
                WindowListService.RecordActiveWindow(fgHwnd);
            }

            var activeWin = WindowListService.GetWindowInfo(fgHwnd, fgHwnd);
            var candidateWindows = WindowListService.GetOpenWindows(fgHwnd, maxCount: 16);

            _currentWheel?.Close();
            _currentWheel = new RadialWheelWindow(Settings, activeWin, candidateWindows);
            _currentWheel.Closed += (_, _) =>
            {
                _currentWheel = null;
                lock (_stateLock)
                {
                    _isWheelOpen = false;
                }
            };

            if (!IsGenerationStillValidForWheel(expectedGeneration))
            {
                _currentWheel.CloseImmediately();
                _currentWheel = null;
                return;
            }

            // Показываем колесо без кражи фокуса (WS_EX_NOACTIVATE / ShowActivated="False")
            _currentWheel.Show();
        }
        catch (Exception ex)
        {
            Logger.LogError("[RadialSwitcher] Ошибка при отображении радиального меню.", ex);
        }
    }
}
