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

    private static bool IsModifierKeyDown(int vKey)
    {
        return (GetAsyncKeyState(vKey) & 0x8000) != 0;
    }

    private readonly ISettingsService _settingsService;
    private readonly IKeyboardHookService _keyboardHookService;

    private IDisposable? _hookSubscription;
    private System.Threading.Timer? _holdTimer;
    private RadialWheelWindow? _currentWheel;

    private bool _isTabHeld;
    private bool _isWheelOpen;
    private readonly object _stateLock = new();

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
        }

        Logger.LogInfo("[RadialSwitcher] Модуль отключен.");
        return Task.CompletedTask;
    }

    private void StartHoldTimer(int delayMs)
    {
        lock (_stateLock)
        {
            _holdTimer?.Dispose();
            _holdTimer = new System.Threading.Timer(OnHoldTimerCallback, null, delayMs, Timeout.Infinite);
        }
    }

    private void StopHoldTimer()
    {
        lock (_stateLock)
        {
            _holdTimer?.Dispose();
            _holdTimer = null;
        }
    }

    private void OnHoldTimerCallback(object? state)
    {
        lock (_stateLock)
        {
            if (!_isTabHeld) return;
            _isWheelOpen = true;
        }

        Application.Current?.Dispatcher.BeginInvoke(OpenWheelWindow);
    }

    private bool OnKeyboardInput(KeyboardHookEventArgs e)
    {
        // Игнорируем синтетические нажатия, сгенерированные нашим приложением
        if (e.IsInjected || e.ExtraInfo == KeyboardHookService.ExtraInfoAppMarker)
        {
            return false;
        }

        // Если нажат Escape при открытом меню или зажатом Tab — мгновенная отмена и закрытие
        if (e.VirtualCode == VK_ESCAPE)
        {
            bool shouldCancel = false;
            lock (_stateLock)
            {
                if (_isTabHeld || _isWheelOpen)
                {
                    shouldCancel = true;
                    _isTabHeld = false;
                    _isWheelOpen = false;
                    StopHoldTimer();
                }
            }

            if (shouldCancel)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    _currentWheel?.CloseImmediately();
                    _currentWheel = null;
                });
                e.SuppressKeyPress = true;
                return true;
            }

            return false;
        }

        if (e.VirtualCode != VK_TAB)
        {
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
                    wasOpen = true;
                    _isTabHeld = false;
                    _isWheelOpen = false;
                    StopHoldTimer();
                }
            }

            if (wasOpen)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    _currentWheel?.CloseImmediately();
                    _currentWheel = null;
                });
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

                    int delay = Math.Max(120, Settings.HoldDelayMs);
                    StartHoldTimer(delay);

                    // Временно подавляем Tab до решения (клик или удержание)
                    e.SuppressKeyPress = true;
                    return true;
                }
                else
                {
                    // Повторные сообщения автоповтора клавиши Windows при удержании Tab
                    if (_isWheelOpen || _isTabHeld)
                    {
                        e.SuppressKeyPress = true;
                        return true;
                    }
                }
            }
        }
        else if (e.IsKeyUp)
        {
            bool wasWheelOpen;
            bool wasTabHeld;

            lock (_stateLock)
            {
                wasWheelOpen = _isWheelOpen;
                wasTabHeld = _isTabHeld;

                _isTabHeld = false;
                _isWheelOpen = false;
                StopHoldTimer();
            }

            if (wasWheelOpen)
            {
                // Пользователь отпустил Tab после удержания: активируем выбранную зону и закрываем колесо
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (_currentWheel != null)
                    {
                        _currentWheel.CloseAndActivateSelected();
                        _currentWheel = null;
                    }
                });

                e.SuppressKeyPress = true;
                return true;
            }
            else if (wasTabHeld)
            {
                // Был короткий одиночный клик по Tab (< 200 мс)
                // Отправляем штатный атомарный системный Tab (Down + Up) в активное приложение
                KeyboardInputSender.SendKeyPress(VK_TAB);

                e.SuppressKeyPress = true;
                return true;
            }
        }

        return false;
    }

    private void OpenWheelWindow()
    {
        try
        {
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

            // Показываем колесо без кражи фокуса (WS_EX_NOACTIVATE / ShowActivated="False")
            _currentWheel.Show();
        }
        catch (Exception ex)
        {
            Logger.LogError("[RadialSwitcher] Ошибка при отображении радиального меню.", ex);
        }
    }
}
