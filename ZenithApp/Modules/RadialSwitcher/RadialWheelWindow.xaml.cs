using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ZenithApp.Modules.RadialSwitcher;

public sealed partial class RadialWheelWindow : Window
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private readonly RadialSwitcherSettings _settings;
    private readonly WindowInfo? _activeWindow;
    private readonly List<ZoneSlotItem> _slots = new();
    private readonly List<System.Windows.Shapes.Path> _sectorPaths = new();
    private readonly List<FrameworkElement> _sectorElements = new();

    private DispatcherTimer? _cursorTimer;
    private int _selectedZoneIndex = -1;
    private bool _isClosing;

    private readonly Brush _defaultSectorFill = new SolidColorBrush(Color.FromArgb(205, 22, 22, 26));
    private readonly Brush _defaultSectorStroke = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));

    private readonly Brush _selectedSectorFill = new SolidColorBrush(Color.FromArgb(240, 215, 160, 45));
    private readonly Brush _selectedSectorStroke = new SolidColorBrush(Color.FromRgb(255, 228, 120));

    private sealed class ZoneSlotItem
    {
        public int Index { get; init; }
        public string DirectionName { get; init; } = string.Empty;
        public WindowInfo? Window { get; set; }
        public RadialZoneConfig? CustomConfig { get; set; }
        public bool IsPinned => CustomConfig?.IsPinned == true;
        public string DisplayTitle => CustomConfig?.CustomLabel 
                                      ?? Window?.Title 
                                      ?? (IsPinned ? CustomConfig?.PinnedProcessName ?? "Закреплено" : "(Пусто)");
        public ImageSource? DisplayIcon => Window?.Icon;
    }

    public RadialWheelWindow(
        RadialSwitcherSettings settings, 
        WindowInfo? activeWindow, 
        List<WindowInfo> candidateWindows)
    {
        InitializeComponent();
        _settings = settings;
        _activeWindow = activeWindow;

        _defaultSectorFill.Freeze();
        _defaultSectorStroke.Freeze();
        _selectedSectorFill.Freeze();
        _selectedSectorStroke.Freeze();

        BuildSlots(candidateWindows);
        RenderWheel();
        SetupActiveCenter();

        Loaded += OnLoaded;
        MouseMove += OnWindowMouseMove;
    }

    private void BuildSlots(List<WindowInfo> candidateWindows)
    {
        _settings.EnsureZonesConfigured();
        int count = _settings.ZoneCount;

        var availableWindows = new Queue<WindowInfo>(candidateWindows);

        for (int i = 0; i < count; i++)
        {
            var config = _settings.CustomZones.FirstOrDefault(c => c.Index == i);

            WindowInfo? matchedWindow = null;

            if (config != null && config.IsPinned && !string.IsNullOrWhiteSpace(config.PinnedProcessName))
            {
                // Ищем окно по имени процесса
                matchedWindow = candidateWindows.FirstOrDefault(w => 
                    w.ProcessName.Equals(config.PinnedProcessName, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                // Динамическое назначение из недавних окон
                while (availableWindows.Count > 0)
                {
                    var cand = availableWindows.Dequeue();
                    // Пропускаем, если оно уже закреплено за другим слотом
                    bool isReservedByPinned = _settings.CustomZones.Any(c => c.IsPinned && 
                        c.PinnedProcessName?.Equals(cand.ProcessName, StringComparison.OrdinalIgnoreCase) == true);

                    if (!isReservedByPinned)
                    {
                        matchedWindow = cand;
                        break;
                    }
                }
            }

            var slot = new ZoneSlotItem
            {
                Index = i,
                DirectionName = config?.DirectionName ?? $"Зона {i + 1}",
                Window = matchedWindow,
                CustomConfig = config
            };

            _slots.Add(slot);
        }
    }

    private void SetupActiveCenter()
    {
        if (_activeWindow != null)
        {
            ImgCenterActiveApp.Source = _activeWindow.Icon;
            TxtCenterActiveTitle.Text = _activeWindow.Title;
        }
        else
        {
            TxtCenterActiveTitle.Text = "Рабочий стол";
        }
    }

    private void RenderWheel()
    {
        SectorsCanvas.Children.Clear();
        _sectorPaths.Clear();
        _sectorElements.Clear();

        int n = _slots.Count;
        double cx = 250;
        double cy = 250;
        double r1 = 80;
        double r2 = 232;
        double sectorAngle = 360.0 / n;
        double gap = 1.6; // Угловой зазор между секторами в градусах

        for (int i = 0; i < n; i++)
        {
            var slot = _slots[i];
            double midAngle = i * sectorAngle;
            double startAngle = midAngle - (sectorAngle / 2.0) + gap;
            double endAngle = midAngle + (sectorAngle / 2.0) - gap;

            double radStart = startAngle * (Math.PI / 180.0);
            double radEnd = endAngle * (Math.PI / 180.0);
            double radMid = midAngle * (Math.PI / 180.0);

            // Annular sector points (0 deg is North, clockwise)
            var p1 = new Point(cx + r1 * Math.Sin(radStart), cy - r1 * Math.Cos(radStart));
            var p2 = new Point(cx + r2 * Math.Sin(radStart), cy - r2 * Math.Cos(radStart));
            var p3 = new Point(cx + r2 * Math.Sin(radEnd), cy - r2 * Math.Cos(radEnd));
            var p4 = new Point(cx + r1 * Math.Sin(radEnd), cy - r1 * Math.Cos(radEnd));

            var figure = new PathFigure
            {
                StartPoint = p1,
                IsClosed = true,
                IsFilled = true
            };

            figure.Segments.Add(new LineSegment(p2, true));
            figure.Segments.Add(new ArcSegment(p3, new Size(r2, r2), 0, (endAngle - startAngle) > 180, SweepDirection.Clockwise, true));
            figure.Segments.Add(new LineSegment(p4, true));
            figure.Segments.Add(new ArcSegment(p1, new Size(r1, r1), 0, (endAngle - startAngle) > 180, SweepDirection.Counterclockwise, true));

            var geom = new PathGeometry();
            geom.Figures.Add(figure);

            var path = new System.Windows.Shapes.Path
            {
                Data = geom,
                Fill = _defaultSectorFill,
                Stroke = _defaultSectorStroke,
                StrokeThickness = 1.5,
                Tag = i
            };

            SectorsCanvas.Children.Add(path);
            _sectorPaths.Add(path);

            // Иконка и текстовая плашка сектора
            double rMid = (r1 + r2) / 2.0;
            double iconCenterX = cx + rMid * Math.Sin(radMid);
            double iconCenterY = cy - rMid * Math.Cos(radMid);

            var itemPanel = new StackPanel
            {
                Width = 76,
                HorizontalAlignment = HorizontalAlignment.Center,
                IsHitTestVisible = false
            };

            var iconBorder = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                Padding = new Thickness(3),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3)
            };

            if (slot.DisplayIcon != null)
            {
                iconBorder.Child = new Image
                {
                    Source = slot.DisplayIcon,
                    Stretch = Stretch.Uniform
                };
            }
            else
            {
                // Заглушка если окно не запущено
                iconBorder.Child = new TextBlock
                {
                    Text = slot.IsPinned ? "★" : "—",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            itemPanel.Children.Add(iconBorder);

            var label = new TextBlock
            {
                Text = slot.DisplayTitle,
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 72
            };
            itemPanel.Children.Add(label);

            Canvas.SetLeft(itemPanel, iconCenterX - 38);
            Canvas.SetTop(itemPanel, iconCenterY - 26);

            SectorsCanvas.Children.Add(itemPanel);
            _sectorElements.Add(itemPanel);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionAtCursor();

        // Анимация плавного появления и масштабирования
        Opacity = 0.0;
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
        BeginAnimation(OpacityProperty, fadeIn);

        RootGrid.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(0.92, 0.92);
        RootGrid.RenderTransform = scale;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(160)));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(160)));

        // Таймер для непрерывного отслеживания курсора по экрану (60 fps)
        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _cursorTimer.Tick += (_, _) => CheckCursorDirection();
        _cursorTimer.Start();
    }

    private void PositionAtCursor()
    {
        if (GetCursorPos(out var pt))
        {
            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            double mouseDipX = pt.X / dpiX;
            double mouseDipY = pt.Y / dpiY;

            double targetLeft = mouseDipX - (Width / 2.0);
            double targetTop = mouseDipY - (Height / 2.0);

            var workArea = SystemParameters.WorkArea;

            if (targetLeft < workArea.Left) targetLeft = workArea.Left;
            if (targetLeft + Width > workArea.Right) targetLeft = workArea.Right - Width;
            if (targetTop < workArea.Top) targetTop = workArea.Top;
            if (targetTop + Height > workArea.Bottom) targetTop = workArea.Bottom - Height;

            Left = targetLeft;
            Top = targetTop;
        }
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        CheckCursorDirection();
    }

    private void CheckCursorDirection()
    {
        if (_isClosing) return;

        if (GetCursorPos(out var pt))
        {
            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            double mouseDipX = pt.X / dpiX;
            double mouseDipY = pt.Y / dpiY;

            double localX = mouseDipX - Left;
            double localY = mouseDipY - Top;

            double dx = localX - 250;
            double dy = localY - 250;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            // Мертвая зона в центре
            if (dist < 65)
            {
                HighlightZone(-1);
            }
            else
            {
                // Угол где 0 - это Север (Вверх), по часовой стрелке
                double rad = Math.Atan2(dx, -dy);
                double deg = rad * (180.0 / Math.PI);
                if (deg < 0) deg += 360.0;

                double sectorSize = 360.0 / _slots.Count;
                int zoneIdx = (int)Math.Floor((deg + sectorSize / 2.0) / sectorSize) % _slots.Count;

                HighlightZone(zoneIdx);
            }
        }
    }

    private void HighlightZone(int index)
    {
        if (_selectedZoneIndex == index) return;
        _selectedZoneIndex = index;

        for (int i = 0; i < _sectorPaths.Count; i++)
        {
            bool isSelected = i == index;
            _sectorPaths[i].Fill = isSelected ? _selectedSectorFill : _defaultSectorFill;
            _sectorPaths[i].Stroke = isSelected ? _selectedSectorStroke : _defaultSectorStroke;
            _sectorPaths[i].StrokeThickness = isSelected ? 2.5 : 1.5;

            if (isSelected)
            {
                _sectorPaths[i].Effect = (Effect)FindResource("GlowEffect");
            }
            else
            {
                _sectorPaths[i].Effect = null;
            }
        }

        if (index >= 0 && index < _slots.Count)
        {
            var slot = _slots[index];
            CenterActivePanel.Visibility = Visibility.Collapsed;
            CenterHoverPanel.Visibility = Visibility.Visible;

            ImgCenterHoverApp.Source = slot.DisplayIcon;
            TxtCenterHoverTitle.Text = slot.DisplayTitle;
        }
        else
        {
            CenterActivePanel.Visibility = Visibility.Visible;
            CenterHoverPanel.Visibility = Visibility.Collapsed;
        }
    }

    public void CloseAndActivateSelected()
    {
        if (_isClosing) return;
        _isClosing = true;

        _cursorTimer?.Stop();
        _cursorTimer = null;

        int selectedIdx = _selectedZoneIndex;

        // Анимация быстрого закрытия
        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(120));
        fadeOut.Completed += (_, _) =>
        {
            Close();

            // Если был выбран валидный сектор: переключаем фокус
            if (selectedIdx >= 0 && selectedIdx < _slots.Count)
            {
                var slot = _slots[selectedIdx];
                if (slot.Window != null && slot.Window.Hwnd != nint.Zero)
                {
                    WindowListService.ActivateWindow(slot.Window.Hwnd);
                }
                else if (slot.IsPinned && _settings.AutoLaunchPinned && !string.IsNullOrWhiteSpace(slot.CustomConfig?.PinnedProcessName))
                {
                    // Попытка запустить закрепленное приложение, если оно еще не открыто
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = slot.CustomConfig.PinnedProcessName,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }
        };

        BeginAnimation(OpacityProperty, fadeOut);
    }
}
