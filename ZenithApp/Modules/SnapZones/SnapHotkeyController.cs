using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Logging;

namespace ZenithApp.Modules.SnapZones;

public sealed class SnapHotkeyController : IDisposable
{
    private readonly IHotkeyService _hotkeyService;
    private readonly IAppLogger _logger;
    private readonly SnapZonesSettings _settings;
    private readonly Func<ZoneLayoutPreset> _presetProvider;
    private readonly Func<CustomLayoutProfile?> _customProfileProvider;

    private readonly List<string> _registeredHotkeyIds = new();

    public SnapHotkeyController(
        IHotkeyService hotkeyService, 
        IAppLogger logger, 
        SnapZonesSettings settings,
        Func<ZoneLayoutPreset> presetProvider,
        Func<CustomLayoutProfile?> customProfileProvider)
    {
        _hotkeyService = hotkeyService;
        _logger = logger;
        _settings = settings;
        _presetProvider = presetProvider;
        _customProfileProvider = customProfileProvider;
    }

    public void RegisterHotkeys()
    {
        UnregisterHotkeys();

        // 1. Move Active Window to Previous Zone (Ctrl + Alt + Left)
        Register("Snap_PrevZone", KeyModifiers.Control | KeyModifiers.Alt, Key.Left, () => CycleZone(-1));

        // 2. Move Active Window to Next Zone (Ctrl + Alt + Right)
        Register("Snap_NextZone", KeyModifiers.Control | KeyModifiers.Alt, Key.Right, () => CycleZone(1));

        // 3. Snap to Zone 1..9 (Ctrl + Alt + 1..9)
        var numKeys = new[] { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9 };
        for (int i = 0; i < numKeys.Length; i++)
        {
            int zoneNum = i + 1;
            Register($"Snap_Zone_{zoneNum}", KeyModifiers.Control | KeyModifiers.Alt, numKeys[i], () => SnapActiveWindowToZoneNumber(zoneNum));
        }
    }

    private void Register(string id, KeyModifiers mod, Key key, Action action)
    {
        if (_hotkeyService.RegisterHotkey(id, mod, key, action))
        {
            _registeredHotkeyIds.Add(id);
        }
        else
        {
            _logger.LogWarning($"[SnapHotkeyController] Горячая клавиша '{id}' ({mod} + {key}) не может быть зарегистрирована (конфликт).");
        }
    }

    public void UnregisterHotkeys()
    {
        foreach (var id in _registeredHotkeyIds)
        {
            _hotkeyService.UnregisterHotkey(id);
        }
        _registeredHotkeyIds.Clear();
    }

    public void CycleZone(int delta)
    {
        var fgHwnd = WindowPlacementService.GetForegroundWindow();
        if (fgHwnd == nint.Zero || !WindowPlacementService.IsSnapCandidate(fgHwnd, _settings.ExcludedProcesses))
        {
            return;
        }

        var mon = MultiMonitorHelper.GetMonitorFromWindow(fgHwnd);
        if (mon == null) return;

        var preset = _presetProvider();
        if (_settings.MonitorPresets.TryGetValue(mon.DeviceName, out var presetName) &&
            Enum.TryParse<ZoneLayoutPreset>(presetName, out var p))
        {
            effectivePreset = p;
        }
        else
        {
            effectivePreset = preset;
        }

        var zones = ZoneCalculator.CalculateZones(mon.WorkArea, effectivePreset, _settings.ZoneSpacing, _settings.ScreenMargin, _customProfileProvider());
        if (zones.Count == 0) return;

        var curBounds = WindowPlacementService.GetExtendedFrameBounds(fgHwnd);
        int curIndex = 0;
        double minDistance = double.MaxValue;

        for (int i = 0; i < zones.Count; i++)
        {
            double dist = Math.Abs(zones[i].Bounds.Left - curBounds.Left) + Math.Abs(zones[i].Bounds.Top - curBounds.Top);
            if (dist < minDistance)
            {
                minDistance = dist;
                curIndex = i;
            }
        }

        int targetIndex = (curIndex + delta) % zones.Count;
        if (targetIndex < 0) targetIndex += zones.Count;

        WindowPlacementService.SnapWindowToZone(fgHwnd, zones[targetIndex].Bounds);
    }

    private ZoneLayoutPreset effectivePreset;

    public void SnapActiveWindowToZoneNumber(int zoneNumber)
    {
        var fgHwnd = WindowPlacementService.GetForegroundWindow();
        if (fgHwnd == nint.Zero || !WindowPlacementService.IsSnapCandidate(fgHwnd, _settings.ExcludedProcesses))
        {
            return;
        }

        var mon = MultiMonitorHelper.GetMonitorFromWindow(fgHwnd);
        if (mon == null) return;

        var preset = _presetProvider();
        if (_settings.MonitorPresets.TryGetValue(mon.DeviceName, out var presetName) &&
            Enum.TryParse<ZoneLayoutPreset>(presetName, out var p))
        {
            effectivePreset = p;
        }
        else
        {
            effectivePreset = preset;
        }

        var zones = ZoneCalculator.CalculateZones(mon.WorkArea, effectivePreset, _settings.ZoneSpacing, _settings.ScreenMargin, _customProfileProvider());
        var matched = zones.Find(z => z.ZoneNumber == zoneNumber);
        if (matched != null)
        {
            WindowPlacementService.SnapWindowToZone(fgHwnd, matched.Bounds);
        }
    }

    public void Dispose()
    {
        UnregisterHotkeys();
    }
}
