using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ZenithApp.Core.Logging;
using ZenithApp.Modules.NextSession;
using ZenithApp.Modules.Screenshot;

namespace ZenithApp.Modules.DynamicIsland;

public partial class DynamicIslandWindow : Window, IDynamicIslandHost
{
    private enum IslandVisualMode
    {
        HiddenNotch,
        CompactIdle,
        DropTarget,
        Notification,
        Countdown,
        ExpandedDrawer
    }

    private const double PopupCardWidth = 416.0;
    private const double ExpandedWindowWidth = 424.0;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly nint HWND_TOPMOST = new(-1);

    private static readonly Brush DragHitBridgeBrush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    private readonly DynamicIslandModule _module;
    private readonly DynamicIslandShelfService _shelfService;
    private readonly IslandMediaService _mediaService;
    private readonly IAppLogger _logger;

    private readonly DispatcherTimer _notificationTimer;
    private readonly DispatcherTimer _autoCollapseTimer;
    private readonly DispatcherTimer _dragLeaveDebounceTimer;
    private readonly DispatcherTimer _countdownTimer;
    private readonly DispatcherTimer _fullscreenCheckTimer;

    private DynamicIslandSettings _settings = new();
    private IslandMediaState _mediaState = IslandMediaState.Empty;
    private IslandVisualMode _currentMode = IslandVisualMode.CompactIdle;
    private IslandVisualMode _modeBeforeDrag = IslandVisualMode.CompactIdle;

    private bool _isClosing;
    private bool _isPointerInside;
    private bool _isDraggingOver;
    private bool _isDraggingOut;
    private bool _isDrawerPinned;
    private bool _isHiddenByFullscreen;
    private int _animationGeneration;

    // Active notification state
    private bool _hasActiveNotification;
    private DateTime _notificationEndUtc;
    private TimeSpan _notificationRemainingOnPause;

    // Active NextSession countdown state
    private bool _hasActiveCountdown;
    private int _countdownTotalSeconds;
    private IReadOnlyList<string> _countdownPrograms = Array.Empty<string>();
    private string? _countdownMessagePreview;
    private readonly Stopwatch _countdownStopwatch = new();
    private TaskCompletionSource<NextSessionCountdownOutcome>? _countdownTcs;
    private CancellationTokenRegistration _countdownCancelReg;

    // Drag-out state
    private Point _dragStartPoint;
    private IslandShelfItem? _pendingDragItem;
    private bool _pendingDragAllFiles;
    private bool _pendingDragAllText;

    public DynamicIslandWindow(
        DynamicIslandModule module,
        DynamicIslandShelfService shelfService,
        IslandMediaService mediaService,
        IAppLogger logger)
    {
        InitializeComponent();

        _module = module;
        _shelfService = shelfService;
        _mediaService = mediaService;
        _logger = logger;

        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _notificationTimer.Tick += OnNotificationTimerTick;

        _autoCollapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) };
        _autoCollapseTimer.Tick += OnAutoCollapseTick;

        _dragLeaveDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(65) };
        _dragLeaveDebounceTimer.Tick += OnDragLeaveDebounceTick;

        _countdownTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _countdownTimer.Tick += OnCountdownTimerTick;

        _fullscreenCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _fullscreenCheckTimer.Tick += OnFullscreenCheckTick;

        UpdateNotchGeometryForMode(_currentMode);

        Loaded += (_, _) =>
        {
            TransitionToBestIdleOrActiveMode();
            _fullscreenCheckTimer.Start();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        nint exStyle = nint.Size == 8 ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE) : GetWindowLong32(hwnd, GWL_EXSTYLE);
        exStyle = (nint)((long)exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        if (nint.Size == 8)
            SetWindowLongPtr64(hwnd, GWL_EXSTYLE, exStyle);
        else
            SetWindowLong32(hwnd, GWL_EXSTYLE, (int)exStyle);
    }

    public void ApplySettings(DynamicIslandSettings settings)
    {
        RunOnUiThread(() =>
        {
            _settings = settings;
            if (!_settings.HideInFullscreen && _isHiddenByFullscreen)
            {
                _isHiddenByFullscreen = false;
                Visibility = Visibility.Visible;
            }
            UpdateMediaVisuals();
            TransitionToBestIdleOrActiveMode();
        });
    }

    public void RefreshShelf()
    {
        RunOnUiThread(() =>
        {
            var items = _shelfService.GetItems();
            ShelfItemsControl.ItemsSource = items;

            bool hasItems = items.Count > 0;
            ShelfEmptyBox.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
            ShelfScrollViewer.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
            BtnClearShelf.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;

            TxtDrawerShelfSummary.Text = hasItems
                ? $"· Элементов: {items.Count}"
                : "· Пусто";

            int fileCount = items.Count(i => i.IsFileOrFolder);
            int textCount = items.Count - fileCount;

            BtnDragAllFiles.Visibility = fileCount >= 2 ? Visibility.Visible : Visibility.Collapsed;
            TxtDragAllFiles.Text = $"Перетащить файлы ({fileCount})";
            Grid.SetColumnSpan(BtnDragAllFiles, textCount >= 2 ? 1 : 2);

            BtnDragAllText.Visibility = textCount >= 2 ? Visibility.Visible : Visibility.Collapsed;
            TxtDragAllText.Text = $"Перетащить текст ({textCount})";
            if (fileCount < 2)
            {
                Grid.SetColumn(BtnDragAllText, 0);
                Grid.SetColumnSpan(BtnDragAllText, 2);
            }
            else
            {
                Grid.SetColumn(BtnDragAllText, 1);
                Grid.SetColumnSpan(BtnDragAllText, 1);
            }

            BulkDragPanel.Visibility = (fileCount >= 2 || textCount >= 2)
                ? Visibility.Visible
                : Visibility.Collapsed;

            UpdateNotchVisuals();
            TransitionToBestIdleOrActiveMode();
        });
    }

    public void UpdateMediaState(IslandMediaState state)
    {
        RunOnUiThread(() =>
        {
            _mediaState = state ?? IslandMediaState.Empty;
            UpdateMediaVisuals();
            UpdateNotchVisuals();
            TransitionToBestIdleOrActiveMode();
        });
    }

    public void ShowNotification(string message, string? title, ToastNotificationType type, int durationSeconds)
    {
        RunOnUiThread(() =>
        {
            if (_isClosing) return;

            EnsureVisibleOverFullscreen();

            TxtNotifTitle.Text = !string.IsNullOrWhiteSpace(title)
                ? title.Trim()
                : type switch
                {
                    ToastNotificationType.Error => "Zenith — Ошибка",
                    ToastNotificationType.Info => "Zenith — Информация",
                    _ => "Zenith Utility"
                };
            TxtNotifMessage.Text = message.Trim();

            var (accent, bg, glyph) = type switch
            {
                ToastNotificationType.Error => (
                    Color.FromRgb(239, 68, 68),
                    Color.FromArgb(40, 239, 68, 68),
                    "✕"),
                ToastNotificationType.Info => (
                    Color.FromRgb(59, 130, 246),
                    Color.FromArgb(40, 59, 130, 246),
                    "i"),
                _ => (
                    Color.FromRgb(16, 185, 129),
                    Color.FromArgb(40, 16, 185, 129),
                    "✓")
            };

            var accentBrush = new SolidColorBrush(accent);
            NotifIconBadge.Background = new SolidColorBrush(bg);
            NotifIconBadge.BorderBrush = accentBrush;
            TxtNotifGlyph.Text = glyph;
            TxtNotifGlyph.Foreground = accentBrush;
            NotifProgressBar.Background = accentBrush;

            var duration = TimeSpan.FromSeconds(Math.Clamp(durationSeconds, 1, 15));
            _hasActiveNotification = true;
            _notificationEndUtc = DateTime.UtcNow.Add(duration);
            _notificationRemainingOnPause = duration;

            AnimateNotifProgressBar(duration);
            _notificationTimer.Start();

            TransitionToBestIdleOrActiveMode();
        });
    }

    public Task<NextSessionCountdownOutcome> WaitWithCountdownAsync(
        int delaySeconds,
        IReadOnlyList<string> programNames,
        string? messagePreview,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<NextSessionCountdownOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

        RunOnUiThread(() =>
        {
            if (_isClosing)
            {
                tcs.TrySetResult(NextSessionCountdownOutcome.Cancelled);
                return;
            }

            FinishActiveCountdown(NextSessionCountdownOutcome.Cancelled);

            _countdownTcs = tcs;
            _countdownTotalSeconds = Math.Max(1, delaySeconds);
            _countdownPrograms = (programNames ?? Array.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .ToList();
            _countdownMessagePreview = messagePreview;
            _hasActiveCountdown = true;

            TxtCountdownSummary.Text = BuildProgramsSummary(_countdownPrograms);
            if (!string.IsNullOrWhiteSpace(_countdownMessagePreview))
            {
                TxtCountdownNote.Text = $"«{_countdownMessagePreview.Trim()}»";
                TxtCountdownNote.Visibility = Visibility.Visible;
            }
            else
            {
                TxtCountdownNote.Visibility = Visibility.Collapsed;
            }

            if (cancellationToken.CanBeCanceled)
            {
                _countdownCancelReg = cancellationToken.Register(() =>
                {
                    RunOnUiThread(() => FinishActiveCountdown(NextSessionCountdownOutcome.Cancelled));
                });
            }

            EnsureVisibleOverFullscreen();
            UpdateCountdownVisuals(0);
            _countdownStopwatch.Restart();
            _countdownTimer.Start();

            TransitionToBestIdleOrActiveMode();
        });

        return tcs.Task;
    }

    public void ExpandDrawer()
    {
        RunOnUiThread(() =>
        {
            if (_isClosing) return;
            EnsureVisibleOverFullscreen();
            _isDrawerPinned = true;
            _autoCollapseTimer.Stop();
            TransitionToBestIdleOrActiveMode();
        });
    }

    public void CloseHost()
    {
        RunOnUiThread(() =>
        {
            if (_isClosing) return;
            _isClosing = true;

            _notificationTimer.Stop();
            _autoCollapseTimer.Stop();
            _dragLeaveDebounceTimer.Stop();
            _countdownTimer.Stop();
            _fullscreenCheckTimer.Stop();
            FinishActiveCountdown(NextSessionCountdownOutcome.Cancelled);

            Close();
        });
    }

    private void TransitionToBestIdleOrActiveMode()
    {
        if (_isClosing) return;

        IslandVisualMode nextMode;
        if (_isDraggingOver)
        {
            nextMode = IslandVisualMode.DropTarget;
        }
        else if (_hasActiveCountdown)
        {
            nextMode = IslandVisualMode.Countdown;
        }
        else if (_hasActiveNotification)
        {
            nextMode = IslandVisualMode.Notification;
        }
        else if (_isDrawerPinned)
        {
            nextMode = IslandVisualMode.ExpandedDrawer;
        }
        else if (_settings.AutoHideWhenIdle && !_isPointerInside && !ShouldKeepCompactVisibleWhenIdle())
        {
            nextMode = IslandVisualMode.HiddenNotch;
        }
        else
        {
            nextMode = IslandVisualMode.CompactIdle;
        }

        ApplyVisualMode(nextMode);
    }

    private bool ShouldKeepCompactVisibleWhenIdle()
    {
        return _settings.ShowMediaControls && _mediaState.HasSession && _mediaState.IsPlaying;
    }

    private static bool IsPopupMode(IslandVisualMode mode)
    {
        return mode is IslandVisualMode.DropTarget
            or IslandVisualMode.Notification
            or IslandVisualMode.Countdown
            or IslandVisualMode.ExpandedDrawer;
    }

    private void ApplyVisualMode(IslandVisualMode mode)
    {
        bool wasPopup = IsPopupMode(_currentMode);
        bool isPopup = IsPopupMode(mode);
        bool modeChanged = _currentMode != mode;
        _currentMode = mode;

        // Верхний трапециевидный выступ /----\ не меняет форму при появлении карточки снизу
        var (notchWidth, notchHeight, slope) = UpdateNotchGeometryForMode(mode);
        UpdateNotchVisuals();

        // Невидимый мостик 1/255 alpha только во время перетаскивания, чтобы зазор между холмиком и полем сброса не мигал DragLeave
        RootGrid.Background = _isDraggingOver ? DragHitBridgeBrush : null;

        if (!isPopup)
        {
            if (wasPopup && PopupCardBorder.Visibility == Visibility.Visible)
            {
                AnimatePopupCardOut(notchWidth, notchHeight);
            }
            else
            {
                HideAllPopupViews();
                PopupCardBorder.Visibility = Visibility.Collapsed;
                SetWindowBoundsAndCenter(notchWidth, notchHeight);
            }
            return;
        }

        // Показываем нужную карточку под трапециевидным выступом
        int gen = ++_animationGeneration;

        ViewDropTarget.Visibility = mode == IslandVisualMode.DropTarget ? Visibility.Visible : Visibility.Collapsed;
        ViewNotification.Visibility = mode == IslandVisualMode.Notification ? Visibility.Visible : Visibility.Collapsed;
        ViewCountdown.Visibility = mode == IslandVisualMode.Countdown ? Visibility.Visible : Visibility.Collapsed;
        ViewExpandedDrawer.Visibility = mode == IslandVisualMode.ExpandedDrawer ? Visibility.Visible : Visibility.Collapsed;

        // Для зоны сброса делаем фон чуть более прозрачным, как стеклянная плашка
        PopupCardBorder.Background = mode == IslandVisualMode.DropTarget
            ? new SolidColorBrush(Color.FromArgb(196, 17, 23, 36))
            : new SolidColorBrush(Color.FromArgb(224, 18, 21, 30));
        PopupCardBorder.BorderBrush = mode == IslandVisualMode.DropTarget
            ? new SolidColorBrush(Color.FromArgb(160, 96, 165, 250))
            : new SolidColorBrush(Color.FromArgb(54, 255, 255, 255));

        PopupCardBorder.Visibility = Visibility.Visible;
        PopupCardBorder.Width = mode == IslandVisualMode.DropTarget ? 360.0 : PopupCardWidth;

        // Измеряем целевую высоту карточки один раз, без дёрганий SizeToContent на каждом кадре
        PopupCardBorder.InvalidateMeasure();
        PopupCardBorder.Measure(new Size(PopupCardBorder.Width, 560));
        double cardHeight = Math.Ceiling(PopupCardBorder.DesiredSize.Height);
        double targetWindowWidth = Math.Max(ExpandedWindowWidth, notchWidth);
        double targetWindowHeight = notchHeight + cardHeight + 4;

        SetWindowBoundsAndCenter(targetWindowWidth, targetWindowHeight);

        if (!wasPopup || modeChanged)
        {
            AnimatePopupCardIn(gen);
        }
        else
        {
            PopupCardBorder.BeginAnimation(OpacityProperty, null);
            PopupTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
            PopupScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            PopupScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            PopupCardBorder.Opacity = 1.0;
            PopupTranslateTransform.Y = 0;
            PopupScaleTransform.ScaleX = 1.0;
            PopupScaleTransform.ScaleY = 1.0;
        }
    }

    private (double Width, double Height, double Slope) UpdateNotchGeometryForMode(IslandVisualMode mode)
    {
        double width;
        double height;
        double slope;

        if (mode == IslandVisualMode.HiddenNotch)
        {
            width = 116;
            height = 9;
            slope = 6;
            ViewHiddenNotch.Visibility = Visibility.Visible;
            ViewCompactNotch.Visibility = Visibility.Collapsed;
        }
        else
        {
            height = 28;
            slope = 15;
            if (_settings.ShowMediaControls && _mediaState.HasSession)
            {
                width = 250;
            }
            else if (_shelfService.Count > 0)
            {
                width = 212;
            }
            else
            {
                width = 186;
            }

            ViewHiddenNotch.Visibility = Visibility.Collapsed;
            ViewCompactNotch.Visibility = Visibility.Visible;
        }

        NotchHost.Width = width;
        NotchHost.Height = height;

        // Чёткая трапеция /----\ (перевёрнутая от верхней границы экрана вниз: \____/)
        var fillGeom = new StreamGeometry();
        using (var ctx = fillGeom.Open())
        {
            ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(width, 0), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(width - slope, height), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(slope, height), isStroked: false, isSmoothJoin: false);
        }
        fillGeom.Freeze();
        NotchFillPath.Data = fillGeom;

        // Обводка только боковых граней и нижней площадки (верхняя граница прилегает к краю экрана)
        var strokeGeom = new StreamGeometry();
        using (var ctx = strokeGeom.Open())
        {
            ctx.BeginFigure(new Point(0.5, 0), isFilled: false, isClosed: false);
            ctx.LineTo(new Point(slope, height - 0.5), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(width - slope, height - 0.5), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(width - 0.5, 0), isStroked: true, isSmoothJoin: false);
        }
        strokeGeom.Freeze();
        NotchStrokePath.Data = strokeGeom;

        return (width, height, slope);
    }

    private void AnimatePopupCardIn(int generation)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(210);

        PopupCardBorder.BeginAnimation(OpacityProperty, null);
        PopupTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
        PopupScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PopupScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        PopupCardBorder.Opacity = 0.0;
        PopupTranslateTransform.Y = -10.0;
        PopupScaleTransform.ScaleX = 0.96;
        PopupScaleTransform.ScaleY = 0.92;

        PopupCardBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease });
        PopupTranslateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10.0, 0.0, duration) { EasingFunction = ease });
        PopupScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1.0, duration) { EasingFunction = ease });
        PopupScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1.0, duration) { EasingFunction = ease });
    }

    private void AnimatePopupCardOut(double targetNotchWidth, double targetNotchHeight)
    {
        int gen = ++_animationGeneration;
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var duration = TimeSpan.FromMilliseconds(150);

        var fadeOut = new DoubleAnimation(PopupCardBorder.Opacity, 0.0, duration) { EasingFunction = ease };
        fadeOut.Completed += (_, _) =>
        {
            if (_isClosing || gen != _animationGeneration || IsPopupMode(_currentMode))
            {
                return;
            }

            HideAllPopupViews();
            PopupCardBorder.Visibility = Visibility.Collapsed;
            SetWindowBoundsAndCenter(targetNotchWidth, targetNotchHeight);
        };

        PopupCardBorder.BeginAnimation(OpacityProperty, fadeOut);
        PopupTranslateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0.0, -8.0, duration) { EasingFunction = ease });
        PopupScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, 0.96, duration) { EasingFunction = ease });
        PopupScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, 0.92, duration) { EasingFunction = ease });
    }

    private void HideAllPopupViews()
    {
        ViewDropTarget.Visibility = Visibility.Collapsed;
        ViewNotification.Visibility = Visibility.Collapsed;
        ViewCountdown.Visibility = Visibility.Collapsed;
        ViewExpandedDrawer.Visibility = Visibility.Collapsed;
    }

    private void UpdateNotchVisuals()
    {
        int shelfCount = _shelfService.Count;
        bool hasMedia = _settings.ShowMediaControls && _mediaState.HasSession;

        if (_isDraggingOver)
        {
            CompactStatusDot.Background = new SolidColorBrush(Color.FromRgb(96, 165, 250));
            TxtCompactStatus.Text = "Сбросьте ниже";
        }
        else if (hasMedia)
        {
            CompactStatusDot.Background = _mediaState.IsPlaying
                ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                : new SolidColorBrush(Color.FromRgb(156, 163, 175));
            TxtCompactStatus.Text = _mediaState.Title;
        }
        else if (shelfCount > 0)
        {
            CompactStatusDot.Background = new SolidColorBrush(Color.FromRgb(59, 130, 246));
            TxtCompactStatus.Text = "Полка";
        }
        else
        {
            CompactStatusDot.Background = new SolidColorBrush(Color.FromRgb(59, 130, 246));
            TxtCompactStatus.Text = "Zenith";
        }

        if (shelfCount > 0)
        {
            TxtCompactShelfCount.Text = shelfCount.ToString();
            CompactShelfBadge.Visibility = Visibility.Visible;
        }
        else
        {
            CompactShelfBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateMediaVisuals()
    {
        bool showMedia = _settings.ShowMediaControls && _mediaState.HasSession;
        MediaCard.Visibility = showMedia ? Visibility.Visible : Visibility.Collapsed;
        if (!showMedia) return;

        TxtMediaTitle.Text = _mediaState.Title;
        TxtMediaArtist.Text = string.IsNullOrWhiteSpace(_mediaState.Artist)
            ? _mediaState.AppName
            : _mediaState.Artist;
        TxtMediaPlayPauseIcon.Text = _mediaState.IsPlaying ? "⏸" : "▶";
        ImgMediaThumb.Source = _mediaState.Thumbnail;
    }

    #region Notification & Countdown Timers

    private void AnimateNotifProgressBar(TimeSpan duration)
    {
        NotifProgressBar.BeginAnimation(WidthProperty, null);
        NotifProgressBar.Width = PopupCardWidth;
        var shrink = new DoubleAnimation(PopupCardWidth, 0, duration);
        NotifProgressBar.BeginAnimation(WidthProperty, shrink);
    }

    private void OnNotificationTimerTick(object? sender, EventArgs e)
    {
        if (!_hasActiveNotification)
        {
            _notificationTimer.Stop();
            return;
        }

        if (_isPointerInside)
        {
            return;
        }

        if (DateTime.UtcNow >= _notificationEndUtc)
        {
            DismissActiveNotification();
        }
    }

    private void DismissActiveNotification()
    {
        _notificationTimer.Stop();
        _hasActiveNotification = false;
        NotifProgressBar.BeginAnimation(WidthProperty, null);
        TransitionToBestIdleOrActiveMode();
    }

    private void OnDismissNotificationClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        DismissActiveNotification();
    }

    private void OnCountdownTimerTick(object? sender, EventArgs e)
    {
        if (!_hasActiveCountdown)
        {
            _countdownTimer.Stop();
            return;
        }

        var totalMs = _countdownTotalSeconds * 1000.0;
        var elapsedMs = _countdownStopwatch.Elapsed.TotalMilliseconds;
        if (elapsedMs >= totalMs)
        {
            UpdateCountdownVisuals(1.0);
            FinishActiveCountdown(NextSessionCountdownOutcome.Completed);
            return;
        }

        UpdateCountdownVisuals(elapsedMs / totalMs);
    }

    private void UpdateCountdownVisuals(double fraction)
    {
        var remainingFraction = 1.0 - Math.Clamp(fraction, 0.0, 1.0);
        var remainingSeconds = Math.Max(0, (int)Math.Ceiling(_countdownTotalSeconds * remainingFraction));

        TxtCountdownSeconds.Text = remainingSeconds.ToString();
        TxtCountdownHeader.Text = _countdownPrograms.Count > 0
            ? $"Запуск через {remainingSeconds} сек"
            : $"Напоминание через {remainingSeconds} сек";

        CountdownProgressBar.Width = Math.Max(0, PopupCardWidth * remainingFraction);
    }

    private void FinishActiveCountdown(NextSessionCountdownOutcome outcome)
    {
        _countdownTimer.Stop();
        _countdownStopwatch.Stop();
        _countdownCancelReg.Dispose();

        var tcs = _countdownTcs;
        _countdownTcs = null;
        _hasActiveCountdown = false;

        tcs?.TrySetResult(outcome);
        TransitionToBestIdleOrActiveMode();
    }

    private void OnCountdownRunNowClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        FinishActiveCountdown(NextSessionCountdownOutcome.RunNow);
    }

    private void OnCountdownCancelClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        FinishActiveCountdown(NextSessionCountdownOutcome.Cancelled);
    }

    private static string BuildProgramsSummary(IReadOnlyList<string> programNames)
    {
        if (programNames.Count == 0)
            return "Показ сохранённой заметки для сеанса";
        if (programNames.Count == 1)
            return $"Приложение: {programNames[0]}";
        if (programNames.Count <= 3)
            return $"Программы ({programNames.Count}): {string.Join(", ", programNames)}";
        return $"Программы ({programNames.Count}): {programNames[0]}, {programNames[1]} (+{programNames.Count - 2})";
    }

    #endregion

    #region Pointer, Expand & Drag-and-Drop Shelf

    private void OnWindowMouseEnter(object sender, MouseEventArgs e)
    {
        _isPointerInside = true;
        _autoCollapseTimer.Stop();

        if (_hasActiveNotification)
        {
            _notificationRemainingOnPause = _notificationEndUtc - DateTime.UtcNow;
            if (_notificationRemainingOnPause < TimeSpan.FromMilliseconds(400))
            {
                _notificationRemainingOnPause = TimeSpan.FromMilliseconds(400);
            }
            NotifProgressBar.BeginAnimation(WidthProperty, null);
        }

        if (_currentMode == IslandVisualMode.HiddenNotch)
        {
            TransitionToBestIdleOrActiveMode();
        }
    }

    private void OnWindowMouseLeave(object sender, MouseEventArgs e)
    {
        _isPointerInside = false;

        if (_hasActiveNotification)
        {
            _notificationEndUtc = DateTime.UtcNow.Add(_notificationRemainingOnPause);
            AnimateNotifProgressBar(_notificationRemainingOnPause);
        }

        if (_isDrawerPinned && !_isDraggingOut && !_isDraggingOver)
        {
            _autoCollapseTimer.Stop();
            _autoCollapseTimer.Interval = TimeSpan.FromMilliseconds(850);
            _autoCollapseTimer.Start();
        }
        else if (_currentMode == IslandVisualMode.CompactIdle && _settings.AutoHideWhenIdle)
        {
            TransitionToBestIdleOrActiveMode();
        }
    }

    private void OnAutoCollapseTick(object? sender, EventArgs e)
    {
        _autoCollapseTimer.Stop();
        if (!_isPointerInside && !_isDraggingOut && !_isDraggingOver)
        {
            _isDrawerPinned = false;
            TransitionToBestIdleOrActiveMode();
        }
    }

    private void OnNotchMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_hasActiveNotification)
        {
            DismissActiveNotification();
            return;
        }

        if (_hasActiveCountdown)
        {
            return;
        }

        if (_isDrawerPinned)
        {
            _isDrawerPinned = false;
            _autoCollapseTimer.Stop();
        }
        else
        {
            _isDrawerPinned = true;
            _autoCollapseTimer.Stop();
        }

        TransitionToBestIdleOrActiveMode();
    }

    private void OnCollapseDrawerClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _isDrawerPinned = false;
        _autoCollapseTimer.Stop();
        TransitionToBestIdleOrActiveMode();
    }

    private void OnAddFromClipboardClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _module.AddClipboardToShelf();
    }

    private void OnClearShelfClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _shelfService.Clear();
    }

    private void OnCopyShelfItemClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: IslandShelfItem item })
        {
            try
            {
                if (item.IsFileOrFolder && !string.IsNullOrWhiteSpace(item.FilePath))
                {
                    var list = new StringCollection { item.FilePath };
                    Clipboard.SetFileDropList(list);
                }
                else if (!string.IsNullOrEmpty(item.TextContent))
                {
                    Clipboard.SetText(item.TextContent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[DynamicIsland] Copy shelf item failed: {ex.Message}");
            }
        }
    }

    private void OnRemoveShelfItemClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: IslandShelfItem item })
        {
            _shelfService.RemoveItem(item.Id);
        }
    }

    private void OnWindowDragEnter(object sender, DragEventArgs e)
    {
        _dragLeaveDebounceTimer.Stop();

        if (_isDraggingOut || !CanAcceptDrop(e.Data))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        EnsureVisibleOverFullscreen();
        if (!_isDraggingOver)
        {
            _modeBeforeDrag = _currentMode;
        }
        _isDraggingOver = true;
        _autoCollapseTimer.Stop();
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;

        if (_currentMode != IslandVisualMode.DropTarget)
        {
            TransitionToBestIdleOrActiveMode();
        }
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        _dragLeaveDebounceTimer.Stop();

        if (_isDraggingOut || !CanAcceptDrop(e.Data))
        {
            e.Effects = DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.Copy;
        }
        e.Handled = true;
    }

    private void OnWindowDragLeave(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!_isDraggingOver) return;

        // Небольшой дебаунс, чтобы перемещение между холмиком и карточкой не закрывало DropTarget
        _dragLeaveDebounceTimer.Stop();
        _dragLeaveDebounceTimer.Start();
    }

    private void OnDragLeaveDebounceTick(object? sender, EventArgs e)
    {
        _dragLeaveDebounceTimer.Stop();
        if (!_isDraggingOver) return;

        if (IsCursorInsideWindowRect())
        {
            return;
        }

        _isDraggingOver = false;
        TransitionToBestIdleOrActiveMode();
    }

    private bool IsCursorInsideWindowRect()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero) return false;
        if (!GetWindowRect(hwnd, out var rect)) return false;
        if (!GetCursorPos(out var pt)) return false;

        return pt.X >= rect.Left && pt.X <= rect.Right &&
               pt.Y >= rect.Top && pt.Y <= rect.Bottom;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        _dragLeaveDebounceTimer.Stop();
        _isDraggingOver = false;
        e.Handled = true;

        if (_isDraggingOut) return;

        bool added = false;
        try
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                e.Data.GetData(DataFormats.FileDrop) is string[] files &&
                files.Length > 0)
            {
                added = _shelfService.AddFiles(files) > 0;
            }
            else if (e.Data.GetDataPresent(DataFormats.UnicodeText) &&
                     e.Data.GetData(DataFormats.UnicodeText) is string unicodeText)
            {
                added = _shelfService.AddText(unicodeText);
            }
            else if (e.Data.GetDataPresent(DataFormats.Text) &&
                     e.Data.GetData(DataFormats.Text) is string plainText)
            {
                added = _shelfService.AddText(plainText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[DynamicIsland] Drop failed: {ex.Message}");
        }

        if (added)
        {
            _isDrawerPinned = true;
            if (!_isPointerInside)
            {
                _autoCollapseTimer.Stop();
                _autoCollapseTimer.Interval = TimeSpan.FromMilliseconds(2200);
                _autoCollapseTimer.Start();
            }
        }
        else if (_modeBeforeDrag == IslandVisualMode.ExpandedDrawer)
        {
            _isDrawerPinned = true;
        }

        TransitionToBestIdleOrActiveMode();
    }

    private static bool CanAcceptDrop(IDataObject? data)
    {
        if (data == null) return false;
        return data.GetDataPresent(DataFormats.FileDrop) ||
               data.GetDataPresent(DataFormats.UnicodeText) ||
               data.GetDataPresent(DataFormats.Text);
    }

    private void OnShelfItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src && FindAncestor<Button>(src) != null)
        {
            _pendingDragItem = null;
            return;
        }

        if (sender is FrameworkElement { DataContext: IslandShelfItem item })
        {
            _dragStartPoint = e.GetPosition(this);
            _pendingDragItem = item;
            _pendingDragAllFiles = false;
            _pendingDragAllText = false;
        }
    }

    private void OnShelfItemMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pendingDragItem == null || _isDraggingOut)
        {
            return;
        }

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _pendingDragItem;
        _pendingDragItem = null;

        var dataObject = DynamicIslandShelfService.CreateDataObjectForItem(item);
        if (dataObject == null)
        {
            _shelfService.PruneMissingFiles();
            return;
        }

        ExecuteDragOut(
            (DependencyObject)sender,
            dataObject,
            item.IsFileOrFolder ? DragDropEffects.Copy | DragDropEffects.Move : DragDropEffects.Copy,
            removeItemId: _settings.RemoveShelfItemAfterDrag ? item.Id : null);
    }

    private void OnShelfItemMouseUp(object sender, MouseButtonEventArgs e)
    {
        _pendingDragItem = null;
    }

    private void OnDragAllFilesMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _pendingDragItem = null;
        _pendingDragAllFiles = true;
        _pendingDragAllText = false;
    }

    private void OnDragAllFilesMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !_pendingDragAllFiles || _isDraggingOut) return;

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _pendingDragAllFiles = false;
        var dataObject = _shelfService.CreateDataObjectForAllFiles();
        if (dataObject != null)
        {
            ExecuteDragOut((DependencyObject)sender, dataObject, DragDropEffects.Copy | DragDropEffects.Move);
        }
    }

    private void OnDragAllTextMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _pendingDragItem = null;
        _pendingDragAllFiles = false;
        _pendingDragAllText = true;
    }

    private void OnDragAllTextMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !_pendingDragAllText || _isDraggingOut) return;

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _pendingDragAllText = false;
        var dataObject = _shelfService.CreateDataObjectForAllText();
        if (dataObject != null)
        {
            ExecuteDragOut((DependencyObject)sender, dataObject, DragDropEffects.Copy);
        }
    }

    private void OnDragAllMouseUp(object sender, MouseButtonEventArgs e)
    {
        _pendingDragAllFiles = false;
        _pendingDragAllText = false;
    }

    private void ExecuteDragOut(DependencyObject source, DataObject dataObject, DragDropEffects allowedEffects, Guid? removeItemId = null)
    {
        _isDraggingOut = true;
        _autoCollapseTimer.Stop();

        try
        {
            var result = DragDrop.DoDragDrop(source, dataObject, allowedEffects);
            if (result != DragDropEffects.None && removeItemId.HasValue)
            {
                _shelfService.RemoveItem(removeItemId.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[DynamicIsland] DragOut failed: {ex.Message}");
        }
        finally
        {
            _isDraggingOut = false;
            _autoCollapseTimer.Interval = TimeSpan.FromMilliseconds(550);
            if (!_isPointerInside)
            {
                _autoCollapseTimer.Start();
            }
        }
    }

    #endregion

    #region Media Controls

    private async void OnMediaPrevClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await _mediaService.PreviousTrackAsync();
    }

    private async void OnMediaPlayPauseClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await _mediaService.TogglePlayPauseAsync();
    }

    private async void OnMediaNextClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await _mediaService.NextTrackAsync();
    }

    #endregion

    #region Positioning & Fullscreen Guard

    private void SetWindowBoundsAndCenter(double dipWidth, double dipHeight)
    {
        Width = dipWidth;
        Height = dipHeight;
        UpdateLayout();

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero) return;

        var hMonitor = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref mi))
        {
            return;
        }

        var workArea = mi.rcWork;
        var dpi = VisualTreeHelper.GetDpi(this);
        double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

        int physicalWidth = (int)Math.Round(dipWidth * scaleX);
        int topOffsetDip = _currentMode == IslandVisualMode.HiddenNotch
            ? 0
            : Math.Clamp(_settings.TopMarginPx, 0, 24);
        int topMargin = (int)Math.Round(topOffsetDip * scaleY);

        int monitorWidth = workArea.Right - workArea.Left;
        int x = workArea.Left + Math.Max(0, (monitorWidth - physicalWidth) / 2);
        int y = workArea.Top + topMargin;

        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void OnFullscreenCheckTick(object? sender, EventArgs e)
    {
        if (_isClosing) return;

        if (!_settings.HideInFullscreen ||
            _hasActiveCountdown ||
            _hasActiveNotification ||
            _isDraggingOver ||
            _isDraggingOut ||
            _isDrawerPinned)
        {
            EnsureVisibleOverFullscreen();
            return;
        }

        bool isFullscreen = IsForegroundWindowFullscreenOnIslandMonitor();
        if (isFullscreen != _isHiddenByFullscreen)
        {
            _isHiddenByFullscreen = isFullscreen;
            Visibility = isFullscreen ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void EnsureVisibleOverFullscreen()
    {
        if (_isHiddenByFullscreen)
        {
            _isHiddenByFullscreen = false;
            Visibility = Visibility.Visible;
        }
    }

    private bool IsForegroundWindowFullscreenOnIslandMonitor()
    {
        var fgHwnd = GetForegroundWindow();
        if (fgHwnd == nint.Zero) return false;

        var myHwnd = new WindowInteropHelper(this).Handle;
        if (fgHwnd == myHwnd) return false;

        var cls = new StringBuilder(128);
        if (GetClassName(fgHwnd, cls, cls.Capacity) > 0)
        {
            var className = cls.ToString();
            if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                return false;
            }
        }

        var islandMonitor = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
        var fgMonitor = MonitorFromWindow(fgHwnd, MONITOR_DEFAULTTONEAREST);
        if (fgMonitor != islandMonitor) return false;

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(islandMonitor, ref mi)) return false;
        if (!GetWindowRect(fgHwnd, out var rect)) return false;

        return rect.Left <= mi.rcMonitor.Left &&
               rect.Top <= mi.rcMonitor.Top &&
               rect.Right >= mi.rcMonitor.Right &&
               rect.Bottom >= mi.rcMonitor.Bottom;
    }

    #endregion

    private void RunOnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.BeginInvoke(action);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T target) return target;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
