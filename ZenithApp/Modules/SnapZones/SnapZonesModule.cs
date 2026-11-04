using System;
using System.Collections.Generic;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;

namespace ZenithApp.Modules.SnapZones;

public sealed class SnapZonesModule : BaseFeatureModule
{
    public const string ModuleId = "snap_zones";
    private const string HotkeyId = "Zenith_AlwaysOnTop_Pin";

    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint WINEVENT_OUTOFCONTEXT = 0;

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin, uint eventMax, nint hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hWinEventHook);

    private delegate void WinEventDelegate(
        nint hWinEventHook, uint eventType, nint hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private readonly IHotkeyService _hotkeyService;
    private readonly ISettingsService _settingsService;

    private readonly Dictionary<nint, PinnedWindowBorderOverlay> _pinnedOverlays = new();
    private WinEventDelegate? _winEventProc;
    private nint _hookLocation = nint.Zero;
    private nint _hookDestroy = nint.Zero;
    private readonly object _lock = new();

    public SnapZonesModule(
        IAppLogger logger, 
        IHotkeyService hotkeyService, 
        ISettingsService settingsService) 
        : base(logger)
    {
        _hotkeyService = hotkeyService;
        _settingsService = settingsService;
    }

    public override string Id => ModuleId;
    public override string Name => "Закрепление окон поверх остальных";
    public override string Description => "Мгновенное закрепление любого активного окна поверх всех приложений (Always on Top) по горячей клавише.";

    public SnapZonesSettings Settings => _settingsService.Current.SnapZones;

    protected override Task OnEnableAsync()
    {
        Logger.LogInfo("[SnapZonesModule] Включение модуля закрепления окон поверх остальных...");
        RegisterHotkey();
        StartHooks();
        Logger.LogInfo("[SnapZonesModule] Модуль успешно запущен.");
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        Logger.LogInfo("[SnapZonesModule] Отключение модуля. Снятие закрепления и освобождение оверлеев...");
        UnregisterHotkey();
        StopHooks();
        UnpinAll();
        Logger.LogInfo("[SnapZonesModule] Модуль остановлен.");
        return Task.CompletedTask;
    }

    public void RegisterHotkey()
    {
        _hotkeyService.UnregisterHotkey(HotkeyId);
        bool ok = _hotkeyService.RegisterHotkey(HotkeyId, Settings.HotkeyModifiers, Settings.HotkeyKey, ToggleActiveWindowPin);
        if (!ok)
        {
            Logger.LogWarning($"[SnapZonesModule] Не удалось зарегистрировать хоткей {Settings.HotkeyModifiers} + {Settings.HotkeyKey}. Возможно конфликт с другим приложением.");
        }
    }

    public void UnregisterHotkey()
    {
        _hotkeyService.UnregisterHotkey(HotkeyId);
    }

    public void ToggleActiveWindowPin()
    {
        var fgHwnd = WindowPinService.GetForegroundWindow();
        if (fgHwnd == nint.Zero) return;

        if (!WindowPinService.IsPinnableWindow(fgHwnd, Settings.ExcludedProcesses))
        {
            Logger.LogWarning($"[SnapZonesModule] Окно (HWND={fgHwnd}) находится в списке исключений или является системным.");
            return;
        }

        lock (_lock)
        {
            if (_pinnedOverlays.TryGetValue(fgHwnd, out var existingOverlay))
            {
                // Снимаем закрепление (Unpin)
                WindowPinService.SetWindowTopmost(fgHwnd, false);
                existingOverlay.Close();
                _pinnedOverlays.Remove(fgHwnd);
                PlaySound(false);
                Logger.LogInfo($"[SnapZonesModule] Снято закрепление с окна HWND={fgHwnd}.");
            }
            else
            {
                // Закрепляем поверх остальных (Pin)
                bool pinned = WindowPinService.SetWindowTopmost(fgHwnd, true);
                if (pinned)
                {
                    if (Settings.ShowBorder)
                    {
                        var color = (Color)ColorConverter.ConvertFromString(Settings.BorderColorHex);
                        var brush = new SolidColorBrush(color);
                        brush.Freeze();

                        var overlay = new PinnedWindowBorderOverlay(fgHwnd, Settings.BorderThickness, brush);
                        overlay.Show();
                        _pinnedOverlays[fgHwnd] = overlay;
                    }
                    PlaySound(true);
                    Logger.LogInfo($"[SnapZonesModule] Окно HWND={fgHwnd} успешно закреплено поверх всех остальных.");
                }
            }
        }
    }

    private void StartHooks()
    {
        if (_hookLocation != nint.Zero) return;

        _winEventProc = OnWinEvent;
        // Отслеживаем перемещение и изменение размера окон
        _hookLocation = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, nint.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        // Отслеживаем закрытие окон, чтобы сразу убрать рамку
        _hookDestroy = SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY, nint.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void StopHooks()
    {
        if (_hookLocation != nint.Zero)
        {
            UnhookWinEvent(_hookLocation);
            _hookLocation = nint.Zero;
        }
        if (_hookDestroy != nint.Zero)
        {
            UnhookWinEvent(_hookDestroy);
            _hookDestroy = nint.Zero;
        }
        _winEventProc = null;
    }

    private void OnWinEvent(nint hWinEventHook, uint eventType, nint hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != 0 || idChild != 0 || hWnd == nint.Zero) return;

        lock (_lock)
        {
            if (!_pinnedOverlays.TryGetValue(hWnd, out var overlay)) return;

            if (eventType == EVENT_OBJECT_DESTROY)
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    lock (_lock)
                    {
                        if (_pinnedOverlays.Remove(hWnd, out var ov))
                        {
                            ov.Close();
                        }
                    }
                }));
            }
            else if (eventType == EVENT_OBJECT_LOCATIONCHANGE)
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    lock (_lock)
                    {
                        if (_pinnedOverlays.TryGetValue(hWnd, out var ov))
                        {
                            ov.UpdatePosition();
                        }
                    }
                }), DispatcherPriority.Render);
            }
        }
    }

    private void PlaySound(bool pin)
    {
        if (!Settings.PlayNotificationSound) return;
        try
        {
            if (pin) SystemSounds.Asterisk.Play();
            else SystemSounds.Hand.Play();
        }
        catch { }
    }

    public void UnpinAll()
    {
        lock (_lock)
        {
            foreach (var kvp in _pinnedOverlays)
            {
                WindowPinService.SetWindowTopmost(kvp.Key, false);
                kvp.Value.Close();
            }
            _pinnedOverlays.Clear();
        }
    }
}
