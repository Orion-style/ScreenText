using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace ZenithApp.Modules.KeyRemap;

/// <summary>
/// Non-activating, click-through top-center OSD banner shown briefly when language is switched via KeyRemap.
/// </summary>
public sealed class LanguageSwitchOsdWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly nint HWND_TOPMOST = new(-1);

    private static readonly TimeSpan DefaultDisplayDuration = TimeSpan.FromSeconds(1);

    private static LanguageSwitchOsdWindow? _activeWindow;

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

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

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

    private readonly TextBlock _codeText;
    private readonly TextBlock _languageNameText;
    private readonly TextBlock _subtitleText;
    private readonly Border _cardBorder;
    private readonly Border _badgeBorder;
    private readonly DispatcherTimer _hideTimer;
    private bool _isClosing;

    private LanguageSwitchOsdWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;

        bool isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

        var cardBackground = new SolidColorBrush(isDark
            ? Color.FromArgb(240, 26, 26, 30)
            : Color.FromArgb(245, 250, 250, 252));
        var cardBorderBrush = new SolidColorBrush(isDark
            ? Color.FromArgb(55, 255, 255, 255)
            : Color.FromArgb(40, 0, 0, 0));
        var primaryTextBrush = isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(22, 22, 26));
        var secondaryTextBrush = new SolidColorBrush(isDark
            ? Color.FromArgb(185, 255, 255, 255)
            : Color.FromArgb(160, 0, 0, 0));
        var badgeBgBrush = new SolidColorBrush(isDark
            ? Color.FromArgb(48, 96, 165, 250)
            : Color.FromArgb(28, 37, 99, 235));
        var badgeBorderBrush = new SolidColorBrush(isDark
            ? Color.FromArgb(110, 96, 165, 250)
            : Color.FromArgb(90, 37, 99, 235));
        var badgeTextBrush = new SolidColorBrush(isDark
            ? Color.FromRgb(147, 197, 253)
            : Color.FromRgb(29, 78, 216));

        _cardBorder = new Border
        {
            Background = cardBackground,
            BorderBrush = cardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 10, 18, 10),
            Margin = new Thickness(16),
            MinWidth = 190,
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                Opacity = isDark ? 0.45 : 0.22,
                ShadowDepth = 3,
                Color = Colors.Black
            }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _badgeBorder = new Border
        {
            Background = badgeBgBrush,
            BorderBrush = badgeBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            MinWidth = 42,
            Height = 36,
            Padding = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        _codeText = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = badgeTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _badgeBorder.Child = _codeText;
        Grid.SetColumn(_badgeBorder, 0);
        grid.Children.Add(_badgeBorder);

        var textStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };

        _languageNameText = new TextBlock
        {
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = primaryTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        _subtitleText = new TextBlock
        {
            FontSize = 11.5,
            Foreground = secondaryTextBrush,
            Margin = new Thickness(0, 1, 0, 0)
        };

        textStack.Children.Add(_languageNameText);
        textStack.Children.Add(_subtitleText);
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        _cardBorder.Child = grid;
        Content = _cardBorder;

        _hideTimer = new DispatcherTimer { Interval = DefaultDisplayDuration };
        _hideTimer.Tick += OnHideTimerTick;

        Loaded += (_, _) => PositionTopCenterOnActiveMonitor();
        Closed += (_, _) =>
        {
            _hideTimer.Stop();
            if (ReferenceEquals(_activeWindow, this))
            {
                _activeWindow = null;
            }
        };
    }

    public static void ShowOrUpdate(KeyboardLayoutInfo layoutInfo, bool layoutChanged)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        void ShowCore()
        {
            if (_activeWindow == null)
            {
                _activeWindow = new LanguageSwitchOsdWindow();
                _activeWindow.UpdateDisplay(layoutInfo, layoutChanged);
                _activeWindow.Show();
                _activeWindow.PositionTopCenterOnActiveMonitor();
                _activeWindow.StartShowAndTimer();
            }
            else
            {
                _activeWindow._isClosing = false;
                _activeWindow.UpdateDisplay(layoutInfo, layoutChanged);
                _activeWindow.UpdateLayout();
                _activeWindow.PositionTopCenterOnActiveMonitor();
                _activeWindow.StartShowAndTimer();
            }
        }

        if (dispatcher.CheckAccess())
        {
            ShowCore();
        }
        else
        {
            dispatcher.BeginInvoke(ShowCore);
        }
    }

    public static void CloseCurrent()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        void CloseCore()
        {
            if (_activeWindow != null)
            {
                var win = _activeWindow;
                _activeWindow = null;
                win._hideTimer.Stop();
                win._isClosing = true;
                win.Close();
            }
        }

        if (dispatcher.CheckAccess())
        {
            CloseCore();
        }
        else
        {
            dispatcher.BeginInvoke(CloseCore);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        nint exStyle = nint.Size == 8 ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE) : GetWindowLong32(hwnd, GWL_EXSTYLE);
        exStyle = (nint)((long)exStyle | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        if (nint.Size == 8)
            SetWindowLongPtr64(hwnd, GWL_EXSTYLE, exStyle);
        else
            SetWindowLong32(hwnd, GWL_EXSTYLE, (int)exStyle);
    }

    private void UpdateDisplay(KeyboardLayoutInfo layoutInfo, bool layoutChanged)
    {
        _codeText.Text = layoutInfo.ShortCode;
        _languageNameText.Text = layoutInfo.DisplayName;
        _subtitleText.Text = layoutChanged ? "Язык изменён" : "Текущая раскладка";
    }

    private void StartShowAndTimer()
    {
        _hideTimer.Stop();

        var fadeIn = new DoubleAnimation(Opacity, 1.0, TimeSpan.FromMilliseconds(110))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fadeIn);

        _hideTimer.Interval = DefaultDisplayDuration;
        _hideTimer.Start();
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        _isClosing = true;

        var fadeOut = new DoubleAnimation(Opacity, 0.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) =>
        {
            if (_isClosing)
            {
                Close();
            }
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void PositionTopCenterOnActiveMonitor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero) return;

        var workArea = GetActiveMonitorWorkArea();

        // First move onto the target monitor so VisualTreeHelper.GetDpi reflects that monitor's DPI
        SetWindowPos(hwnd, HWND_TOPMOST, workArea.Left, workArea.Top, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(this);
        double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

        int physicalWidth = (int)Math.Round(ActualWidth * scaleX);
        int topMargin = (int)Math.Round(16 * scaleY);

        int monitorWidth = workArea.Right - workArea.Left;
        int x = workArea.Left + Math.Max(0, (monitorWidth - physicalWidth) / 2);
        int y = workArea.Top + topMargin;

        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private static RECT GetActiveMonitorWorkArea()
    {
        var fgHwnd = GetForegroundWindow();
        var hMonitor = MonitorFromWindow(fgHwnd, MONITOR_DEFAULTTONEAREST);

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(hMonitor, ref mi))
        {
            return mi.rcWork;
        }

        var fallback = SystemParameters.WorkArea;
        return new RECT
        {
            Left = (int)Math.Round(fallback.Left),
            Top = (int)Math.Round(fallback.Top),
            Right = (int)Math.Round(fallback.Right),
            Bottom = (int)Math.Round(fallback.Bottom)
        };
    }
}
