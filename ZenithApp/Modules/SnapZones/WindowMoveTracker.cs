using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using ZenithApp.Core.Logging;

namespace ZenithApp.Modules.SnapZones;

public sealed class WindowMoveTracker : IDisposable
{
    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint WINEVENT_OUTOFCONTEXT = 0;

    private const int VK_SHIFT = 0x10;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin, uint eventMax, nint hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hWinEventHook);

    private delegate void WinEventDelegate(
        nint hWinEventHook, uint eventType, nint hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private readonly IAppLogger _logger;
    private readonly SnapZonesSettings _settings;
    private readonly Func<ZoneLayoutPreset> _presetProvider;
    private readonly Func<CustomLayoutProfile?> _customProfileProvider;

    private WinEventDelegate? _winEventProc;
    private nint _hookStart = nint.Zero;
    private nint _hookEnd = nint.Zero;

    private nint _currentDraggingHwnd = nint.Zero;
    private Rect? _originalWindowBounds;
    private DispatcherTimer? _dragPollingTimer;
    private readonly List<ZoneOverlayWindow> _activeOverlays = new();

    private ZoneOverlayWindow? _activeTargetOverlay;
    private ZoneRect? _activeTargetZone;
    private bool _isShiftHeldDuringDrag;

    // Cache of original unsnapped sizes for windows that were snapped
    private readonly Dictionary<nint, Rect> _preSnapBoundsCache = new();

    public event Action<nint, Rect>? WindowSnapped;

    public WindowMoveTracker(
        IAppLogger logger, 
        SnapZonesSettings settings,
        Func<ZoneLayoutPreset> presetProvider,
        Func<CustomLayoutProfile?> customProfileProvider)
    {
        _logger = logger;
        _settings = settings;
        _presetProvider = presetProvider;
        _customProfileProvider = customProfileProvider;
    }

    public void StartTracking()
    {
        if (_hookStart != nint.Zero) return;

        _winEventProc = OnWinEvent;
        _hookStart = SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZESTART, nint.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        _hookEnd = SetWinEventHook(EVENT_SYSTEM_MOVESIZEEND, EVENT_SYSTEM_MOVESIZEEND, nint.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

        _logger.LogInfo("[WindowMoveTracker] Хуки перемещения окон успешно установлены.");
    }

    public void StopTracking()
    {
        if (_hookStart != nint.Zero)
        {
            UnhookWinEvent(_hookStart);
            _hookStart = nint.Zero;
        }
        if (_hookEnd != nint.Zero)
        {
            UnhookWinEvent(_hookEnd);
            _hookEnd = nint.Zero;
        }
        _winEventProc = null;

        CleanupDragSession();
        _logger.LogInfo("[WindowMoveTracker] Хуки перемещения окон сняты.");
    }

    private void OnWinEvent(nint hWinEventHook, uint eventType, nint hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Filter out non-window objects
        if (idObject != 0 || idChild != 0 || hWnd == nint.Zero) return;

        if (eventType == EVENT_SYSTEM_MOVESIZESTART)
        {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                OnMoveSizeStart(hWnd);
            }));
        }
        else if (eventType == EVENT_SYSTEM_MOVESIZEEND)
        {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                OnMoveSizeEnd(hWnd);
            }));
        }
    }

    public void OnMoveSizeStart(nint hWnd)
    {
        if (!WindowPlacementService.IsSnapCandidate(hWnd, _settings.ExcludedProcesses))
        {
            return;
        }

        _currentDraggingHwnd = hWnd;
        _originalWindowBounds = WindowPlacementService.GetExtendedFrameBounds(hWnd);

        // Check if window was previously snapped, and if user unsnaps it, restore original size
        if (_settings.RestoreOriginalSizeOnUnsnap && _preSnapBoundsCache.TryGetValue(hWnd, out var preSnapBounds))
        {
            // Restore original dimension keeping cursor position
            MultiMonitorHelper.GetCursorPos(out var curPos);
            int origW = (int)preSnapBounds.Width;
            int origH = (int)preSnapBounds.Height;
            int newLeft = curPos.X - (origW / 2);
            int newTop = curPos.Y - 15;
            WindowPlacementService.SnapWindowToZone(hWnd, new Rect(newLeft, newTop, origW, origH));
            _preSnapBoundsCache.Remove(hWnd);
        }

        // Initialize drag timer (30 ms polling for cursor & Shift key)
        _dragPollingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _dragPollingTimer.Tick += OnDragTimerTick;
        _dragPollingTimer.Start();

        // If AlwaysOnDrag mode, show overlays immediately
        if (_settings.TriggerMode == SnapTriggerMode.AlwaysOnDrag)
        {
            ShowOverlays();
        }
    }

    private void OnDragTimerTick(object? sender, EventArgs e)
    {
        if (_currentDraggingHwnd == nint.Zero) return;

        bool isShift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
        _isShiftHeldDuringDrag = isShift;

        bool shouldShow = _settings.TriggerMode == SnapTriggerMode.AlwaysOnDrag || isShift;

        if (shouldShow && _activeOverlays.Count == 0)
        {
            ShowOverlays();
        }
        else if (!shouldShow && _activeOverlays.Count > 0)
        {
            HideOverlays();
        }

        if (_activeOverlays.Count > 0)
        {
            UpdateCursorZoneHover();
        }
    }

    private void ShowOverlays()
    {
        if (_activeOverlays.Count > 0) return;

        var monitors = MultiMonitorHelper.GetAllMonitors();
        var preset = _presetProvider();
        var customProfile = _customProfileProvider();

        foreach (var mon in monitors)
        {
            // Check if monitor has custom preset configured
            var effectivePreset = preset;
            if (_settings.MonitorPresets.TryGetValue(mon.DeviceName, out var presetName) &&
                Enum.TryParse<ZoneLayoutPreset>(presetName, out var p))
            {
                effectivePreset = p;
            }

            var zones = ZoneCalculator.CalculateZones(mon.WorkArea, effectivePreset, _settings.ZoneSpacing, _settings.ScreenMargin, customProfile);
            var overlay = new ZoneOverlayWindow(mon, zones);
            overlay.Show();
            _activeOverlays.Add(overlay);
        }
    }

    private void HideOverlays()
    {
        foreach (var ov in _activeOverlays)
        {
            ov.Close();
        }
        _activeOverlays.Clear();
        _activeTargetOverlay = null;
        _activeTargetZone = null;
    }

    private void UpdateCursorZoneHover()
    {
        if (!MultiMonitorHelper.GetCursorPos(out var pt)) return;
        var cursorPoint = new Point(pt.X, pt.Y);

        ZoneOverlayWindow? matchedOverlay = null;
        ZoneRect? matchedZone = null;

        foreach (var overlay in _activeOverlays)
        {
            if (overlay.Monitor.MonitorBounds.Contains(cursorPoint))
            {
                matchedOverlay = overlay;
                foreach (var z in overlay.Zones)
                {
                    if (z.Bounds.Contains(cursorPoint))
                    {
                        matchedZone = z;
                        break;
                    }
                }
                break;
            }
        }

        _activeTargetOverlay = matchedOverlay;
        _activeTargetZone = matchedZone;

        foreach (var ov in _activeOverlays)
        {
            if (ov == matchedOverlay && matchedZone != null)
            {
                ov.HighlightZone(matchedZone.ZoneNumber);
            }
            else
            {
                ov.HighlightZone(-1);
            }
        }
    }

    public void OnMoveSizeEnd(nint hWnd)
    {
        if (_currentDraggingHwnd == nint.Zero || _currentDraggingHwnd != hWnd)
        {
            CleanupDragSession();
            return;
        }

        bool shouldSnap = false;
        if (_settings.TriggerMode == SnapTriggerMode.ShiftDrag)
        {
            shouldSnap = _isShiftHeldDuringDrag && _activeTargetZone != null;
        }
        else
        {
            shouldSnap = _activeTargetZone != null;
        }

        if (shouldSnap && _activeTargetZone != null)
        {
            if (_originalWindowBounds.HasValue && !_preSnapBoundsCache.ContainsKey(hWnd))
            {
                _preSnapBoundsCache[hWnd] = _originalWindowBounds.Value;
            }

            WindowPlacementService.SnapWindowToZone(hWnd, _activeTargetZone.Bounds);
            WindowSnapped?.Invoke(hWnd, _activeTargetZone.Bounds);
        }

        CleanupDragSession();
    }

    private void CleanupDragSession()
    {
        _currentDraggingHwnd = nint.Zero;
        _originalWindowBounds = null;
        _activeTargetOverlay = null;
        _activeTargetZone = null;

        if (_dragPollingTimer != null)
        {
            _dragPollingTimer.Stop();
            _dragPollingTimer = null;
        }

        HideOverlays();
    }

    public void Dispose()
    {
        StopTracking();
        _preSnapBoundsCache.Clear();
    }
}
