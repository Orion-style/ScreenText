using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ZenithApp.Modules.SnapZones;

public partial class PinnedWindowBorderOverlay : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    private readonly nint _targetHwnd;
    private readonly double _dpiScaleX = 1.0;
    private readonly double _dpiScaleY = 1.0;

    public nint TargetHwnd => _targetHwnd;

    public PinnedWindowBorderOverlay(nint targetHwnd, int thickness, Brush colorBrush)
    {
        InitializeComponent();
        _targetHwnd = targetHwnd;

        BorderIndicator.BorderThickness = new Thickness(thickness);
        BorderIndicator.BorderBrush = colorBrush;

        // Determine DPI of primary screen / target window
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }

        Loaded += OnLoaded;
        UpdatePosition();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        nint exStyle = nint.Size == 8 ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE) : GetWindowLong32(hwnd, GWL_EXSTYLE);
        exStyle = (nint)((long)exStyle | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        if (nint.Size == 8)
            SetWindowLongPtr64(hwnd, GWL_EXSTYLE, exStyle);
        else
            SetWindowLong32(hwnd, GWL_EXSTYLE, (int)exStyle);

        UpdatePosition();
    }

    public void UpdatePosition()
    {
        if (!WindowPinService.IsWindow(_targetHwnd) || !WindowPinService.IsWindowVisible(_targetHwnd))
        {
            Hide();
            return;
        }

        var bounds = WindowPinService.GetExtendedFrameBounds(_targetHwnd);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            Hide();
            return;
        }

        // Adjust for border placement right over the window frame in DIPs
        double dipX = bounds.Left / _dpiScaleX;
        double dipY = bounds.Top / _dpiScaleY;
        double dipW = bounds.Width / _dpiScaleX;
        double dipH = bounds.Height / _dpiScaleY;

        Left = dipX;
        Top = dipY;
        Width = dipW;
        Height = dipH;

        if (Visibility != Visibility.Visible)
        {
            Show();
        }
    }
}
