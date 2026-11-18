using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace ZenithApp.Modules.RadialSwitcher;

public sealed partial class RadialWheelConfigControl : UserControl
{
    private static readonly Brush _defaultFill = new SolidColorBrush(Color.FromArgb(190, 26, 26, 32));
    private static readonly Brush _defaultStroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));

    private static readonly Brush _hoverFill = new SolidColorBrush(Color.FromArgb(220, 42, 42, 54));
    private static readonly Brush _hoverStroke = new SolidColorBrush(Color.FromArgb(140, 229, 193, 88));

    private static readonly Brush _selectedFill = new SolidColorBrush(Color.FromArgb(220, 65, 52, 22));
    private static readonly Brush _selectedStroke = new SolidColorBrush(Color.FromRgb(255, 228, 120));

    private static readonly Brush _accentGold = new SolidColorBrush(Color.FromRgb(229, 193, 88));

    static RadialWheelConfigControl()
    {
        _defaultFill.Freeze();
        _defaultStroke.Freeze();
        _hoverFill.Freeze();
        _hoverStroke.Freeze();
        _selectedFill.Freeze();
        _selectedStroke.Freeze();
        _accentGold.Freeze();
    }

    public RadialWheelConfigControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SubscribeViewModel();
        RedrawWheel();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is RadialSwitcherSettingsViewModel oldVm)
        {
            oldVm.OnWheelRedrawRequested -= RedrawWheel;
        }

        SubscribeViewModel();
        RedrawWheel();
    }

    private void SubscribeViewModel()
    {
        if (DataContext is RadialSwitcherSettingsViewModel vm)
        {
            vm.OnWheelRedrawRequested -= RedrawWheel;
            vm.OnWheelRedrawRequested += RedrawWheel;
        }
    }

    private void UnsubscribeViewModel()
    {
        if (DataContext is RadialSwitcherSettingsViewModel vm)
        {
            vm.OnWheelRedrawRequested -= RedrawWheel;
        }
    }

    public void RedrawWheel()
    {
        WheelCanvas.Children.Clear();

        if (DataContext is not RadialSwitcherSettingsViewModel vm || vm.Zones == null || vm.Zones.Count == 0)
        {
            return;
        }

        int n = vm.Zones.Count;
        double cx = 135;
        double cy = 135;
        double r1 = 44;
        double r2 = 124;
        double sectorAngle = 360.0 / n;
        double gap = 2.0;

        for (int i = 0; i < n; i++)
        {
            var zone = vm.Zones[i];
            bool isSelected = vm.SelectedZone?.Index == zone.Index;

            double midAngle = i * sectorAngle;
            double startAngle = midAngle - (sectorAngle / 2.0) + gap;
            double endAngle = midAngle + (sectorAngle / 2.0) - gap;

            double radStart = startAngle * (Math.PI / 180.0);
            double radEnd = endAngle * (Math.PI / 180.0);
            double radMid = midAngle * (Math.PI / 180.0);

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

            var path = new Path
            {
                Data = geom,
                Fill = isSelected ? _selectedFill : _defaultFill,
                Stroke = isSelected ? _selectedStroke : _defaultStroke,
                StrokeThickness = isSelected ? 2.2 : 1.2,
                Cursor = Cursors.Hand,
                Tag = zone
            };

            if (isSelected)
            {
                path.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(229, 193, 88),
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.7
                };
            }

            path.MouseEnter += (_, _) =>
            {
                if (vm.SelectedZone?.Index != zone.Index)
                {
                    path.Fill = _hoverFill;
                    path.Stroke = _hoverStroke;
                }
            };

            path.MouseLeave += (_, _) =>
            {
                if (vm.SelectedZone?.Index != zone.Index)
                {
                    path.Fill = _defaultFill;
                    path.Stroke = _defaultStroke;
                }
            };

            path.MouseLeftButtonDown += (_, _) =>
            {
                vm.RequestOpenProgramPicker(zone);
            };

            WheelCanvas.Children.Add(path);

            // Значок по центру сектора
            double rMid = (r1 + r2) / 2.0;
            double badgeCx = cx + rMid * Math.Sin(radMid);
            double badgeCy = cy - rMid * Math.Cos(radMid);

            var badgeBorder = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                Cursor = Cursors.Hand
            };

            if (zone.IsPinned)
            {
                badgeBorder.Background = new SolidColorBrush(Color.FromArgb(220, 48, 40, 22));
                badgeBorder.BorderBrush = _accentGold;
                badgeBorder.BorderThickness = new Thickness(1.4);
                badgeBorder.ToolTip = $"{zone.DirectionName}: {zone.PinnedProcessName} (Закреплено)";

                if (zone.PinnedIcon != null)
                {
                    badgeBorder.Child = new Image
                    {
                        Source = zone.PinnedIcon,
                        Width = 20,
                        Height = 20,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
                else
                {
                    badgeBorder.Child = new TextBlock
                    {
                        Text = "★",
                        FontSize = 13,
                        Foreground = _accentGold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
            }
            else
            {
                badgeBorder.Background = new SolidColorBrush(Color.FromArgb(160, 42, 42, 52));
                badgeBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
                badgeBorder.BorderThickness = new Thickness(1.0);
                badgeBorder.ToolTip = $"{zone.DirectionName}: Свободно. Нажмите «+», чтобы закрепить приложение";

                badgeBorder.Child = new TextBlock
                {
                    Text = "+",
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Foreground = _accentGold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -2, 0, 0)
                };
            }

            badgeBorder.MouseLeftButtonDown += (_, _) =>
            {
                vm.RequestOpenProgramPicker(zone);
            };

            Canvas.SetLeft(badgeBorder, badgeCx - 16);
            Canvas.SetTop(badgeBorder, badgeCy - 16);

            WheelCanvas.Children.Add(badgeBorder);
        }
    }
}
