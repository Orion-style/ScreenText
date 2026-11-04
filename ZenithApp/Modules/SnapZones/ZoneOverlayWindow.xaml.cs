using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ZenithApp.Modules.SnapZones;

public partial class ZoneOverlayWindow : Window
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

    private readonly MonitorInfoData _monitor;
    private readonly List<ZoneRect> _zones;
    private readonly Dictionary<int, Border> _zoneBorders = new();
    private int _highlightedZoneNumber = -1;

    public MonitorInfoData Monitor => _monitor;
    public List<ZoneRect> Zones => _zones;

    public ZoneOverlayWindow(MonitorInfoData monitor, List<ZoneRect> zones)
    {
        InitializeComponent();
        _monitor = monitor;
        _zones = zones;

        // Position window covering monitor bounds
        // Note: Window coordinates in WPF are in DIPs
        Left = _monitor.MonitorBounds.Left / _monitor.DpiScaleX;
        Top = _monitor.MonitorBounds.Top / _monitor.DpiScaleY;
        Width = _monitor.MonitorBounds.Width / _monitor.DpiScaleX;
        Height = _monitor.MonitorBounds.Height / _monitor.DpiScaleY;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Set WS_EX_TOOLWINDOW, WS_EX_TRANSPARENT, WS_EX_NOACTIVATE
        var hwnd = new WindowInteropHelper(this).Handle;
        nint exStyle = nint.Size == 8 ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE) : GetWindowLong32(hwnd, GWL_EXSTYLE);
        exStyle = (nint)((long)exStyle | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        if (nint.Size == 8)
            SetWindowLongPtr64(hwnd, GWL_EXSTYLE, exStyle);
        else
            SetWindowLong32(hwnd, GWL_EXSTYLE, (int)exStyle);

        RenderZones();
        AnimateFadeIn();
    }

    private void RenderZones()
    {
        OverlayCanvas.Children.Clear();
        _zoneBorders.Clear();

        var normalFill = new SolidColorBrush(Color.FromArgb(40, 0, 120, 215));
        normalFill.Freeze();
        var normalStroke = new SolidColorBrush(Color.FromArgb(160, 0, 120, 215));
        normalStroke.Freeze();

        foreach (var zone in _zones)
        {
            // Convert physical zone coordinates to local overlay DIPs
            double localX = (zone.Bounds.Left - _monitor.MonitorBounds.Left) / _monitor.DpiScaleX;
            double localY = (zone.Bounds.Top - _monitor.MonitorBounds.Top) / _monitor.DpiScaleY;
            double localW = zone.Bounds.Width / _monitor.DpiScaleX;
            double localH = zone.Bounds.Height / _monitor.DpiScaleY;

            var border = new Border
            {
                Width = Math.Max(10, localW),
                Height = Math.Max(10, localH),
                CornerRadius = new CornerRadius(8),
                Background = normalFill,
                BorderBrush = normalStroke,
                BorderThickness = new Thickness(1.5),
                Tag = zone.ZoneNumber
            };

            var textBlock = new TextBlock
            {
                Text = zone.ZoneNumber.ToString(),
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            textBlock.Foreground.Freeze();

            border.Child = textBlock;

            Canvas.SetLeft(border, localX);
            Canvas.SetTop(border, localY);

            OverlayCanvas.Children.Add(border);
            _zoneBorders[zone.ZoneNumber] = border;
        }
    }

    public void HighlightZone(int zoneNumber)
    {
        if (_highlightedZoneNumber == zoneNumber) return;
        _highlightedZoneNumber = zoneNumber;

        var normalFill = new SolidColorBrush(Color.FromArgb(40, 0, 120, 215));
        normalFill.Freeze();
        var normalStroke = new SolidColorBrush(Color.FromArgb(160, 0, 120, 215));
        normalStroke.Freeze();

        var highlightFill = new SolidColorBrush(Color.FromArgb(100, 0, 150, 255));
        highlightFill.Freeze();
        var highlightStroke = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255));
        highlightStroke.Freeze();

        foreach (var kvp in _zoneBorders)
        {
            if (kvp.Key == zoneNumber)
            {
                kvp.Value.Background = highlightFill;
                kvp.Value.BorderBrush = highlightStroke;
                kvp.Value.BorderThickness = new Thickness(2.5);
            }
            else
            {
                kvp.Value.Background = normalFill;
                kvp.Value.BorderBrush = normalStroke;
                kvp.Value.BorderThickness = new Thickness(1.5);
            }
        }
    }

    private void AnimateFadeIn()
    {
        Opacity = 0;
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, anim);
    }
}
