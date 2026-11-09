using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
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

    private readonly ISettingsService _settingsService;
    private readonly IKeyboardHookService _keyboardHookService;

    private IDisposable? _hookSubscription;
    private DispatcherTimer? _holdTimer;
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
        Logger.LogInfo("[RadialSwitcher] Модуль активирован. Перехват зажатия Tab включен.");
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        _hookSubscription?.Dispose();
        _hookSubscription = null;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            _holdTimer?.Stop();
            _holdTimer = null;
            _currentWheel?.Close();
            _currentWheel = null;
        });

        _isTabHeld = false;
        _isWheelOpen = false;

        Logger.LogInfo("[RadialSwitcher] Модуль отключен.");
        return Task.CompletedTask;
    }

    private bool OnKeyboardInput(KeyboardHookEventArgs e)
    {
        // Игнорируем синтетические нажатия, сгенерированные нашим приложением
        if (e.IsInjected || e.ExtraInfo == KeyboardHookService.ExtraInfoAppMarker)
        {
            return false;
        }

        if (e.VirtualCode != VK_TAB)
        {
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

                    // Запуск таймера определения зажатия
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        _holdTimer?.Stop();
                        _holdTimer = new DispatcherTimer
                        {
                            Interval = TimeSpan.FromMilliseconds(Math.Max(120, Settings.HoldDelayMs))
                        };
                        _holdTimer.Tick += OnHoldTimerTick;
                        _holdTimer.Start();
                    });

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
            }

            Application.Current?.Dispatcher.Invoke(() =>
            {
                _holdTimer?.Stop();
                _holdTimer = null;
            });

            if (wasWheelOpen)
            {
                // Пользователь отпустил Tab после удержания: активируем выбранную зону и закрываем колесо
                Application.Current?.Dispatcher.Invoke(() =>
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
                // Отправляем штатный системный Tab в активное приложение
                KeyboardInputSender.SendKeyDown(VK_TAB);
                KeyboardInputSender.SendKeyUp(VK_TAB);

                e.SuppressKeyPress = true;
                return true;
            }
        }

        return false;
    }

    private void OnHoldTimerTick(object? sender, EventArgs e)
    {
        _holdTimer?.Stop();
        _holdTimer = null;

        lock (_stateLock)
        {
            if (!_isTabHeld) return;
            _isWheelOpen = true;
        }

        OpenWheelWindow();
    }

    private void OpenWheelWindow()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            try
            {
                var fgHwnd = WindowListService.GetForegroundWindow();
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

                _currentWheel.Show();
                _currentWheel.Activate();
            }
            catch (Exception ex)
            {
                Logger.LogError("[RadialSwitcher] Ошибка при отображении радиального меню.", ex);
            }
        });
    }
}
