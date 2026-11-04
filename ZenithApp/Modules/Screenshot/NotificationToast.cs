using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ZenithApp.Modules.Screenshot;

public enum ToastNotificationType
{
    Success,
    Error,
    Info
}

public static class NotificationToast
{
    public static void Show(string message, string? title = null, ToastNotificationType type = ToastNotificationType.Success, int durationSeconds = 3)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize
            };

            var outerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(240, 26, 26, 26)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 14, 10),
                Margin = new Thickness(12),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 14,
                    Opacity = 0.45,
                    ShadowDepth = 2,
                    Color = Colors.Black
                }
            };

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Иконка статуса (галочка, крестик или инфо)
            var iconContainer = CreateStatusIcon(type);
            Grid.SetColumn(iconContainer, 0);
            mainGrid.Children.Add(iconContainer);

            var textStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 280,
                Margin = new Thickness(10, 0, 0, 0)
            };

            if (!string.IsNullOrEmpty(title))
            {
                textStack.Children.Add(new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12.5,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 2),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            textStack.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 60,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            Grid.SetColumn(textStack, 1);
            mainGrid.Children.Add(textStack);

            outerBorder.Child = mainGrid;
            window.Content = outerBorder;

            var primaryArea = SystemParameters.WorkArea;
            window.Loaded += (_, _) =>
            {
                window.Left = primaryArea.Right - window.ActualWidth - 16;
                window.Top = primaryArea.Bottom - window.ActualHeight - 16;

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(durationSeconds) };
                timer.Tick += (s, ev) =>
                {
                    timer.Stop();
                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(250));
                    fadeOut.Completed += (s2, ev2) => window.Close();
                    window.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                };
                timer.Start();
            };

            window.Show();
        });
    }

    private static FrameworkElement CreateStatusIcon(ToastNotificationType type)
    {
        var container = new Grid
        {
            Width = 26,
            Height = 26,
            VerticalAlignment = VerticalAlignment.Center
        };

        var circle = new Ellipse();

        switch (type)
        {
            case ToastNotificationType.Success:
                circle.Fill = new SolidColorBrush(Color.FromArgb(35, 16, 185, 129));
                circle.Stroke = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                circle.StrokeThickness = 1.5;
                container.Children.Add(circle);

                // Галочка
                var checkmark = new Path
                {
                    Data = Geometry.Parse("M 7 13 L 11 17 L 19 9"),
                    Stroke = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                    StrokeThickness = 2,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                container.Children.Add(checkmark);
                break;

            case ToastNotificationType.Error:
                circle.Fill = new SolidColorBrush(Color.FromArgb(35, 239, 68, 68));
                circle.Stroke = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                circle.StrokeThickness = 1.5;
                container.Children.Add(circle);

                // Крестик
                var crossGroup = new GeometryGroup();
                crossGroup.Children.Add(new LineGeometry(new Point(8, 8), new Point(18, 18)));
                crossGroup.Children.Add(new LineGeometry(new Point(18, 8), new Point(8, 18)));

                var cross = new Path
                {
                    Data = crossGroup,
                    Stroke = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                    StrokeThickness = 2,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                container.Children.Add(cross);
                break;

            case ToastNotificationType.Info:
            default:
                circle.Fill = new SolidColorBrush(Color.FromArgb(35, 59, 130, 246));
                circle.Stroke = new SolidColorBrush(Color.FromRgb(59, 130, 246));
                circle.StrokeThickness = 1.5;
                container.Children.Add(circle);

                var infoText = new TextBlock
                {
                    Text = "i",
                    FontFamily = new FontFamily("Georgia, Times New Roman, serif"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                container.Children.Add(infoText);
                break;
        }

        return container;
    }
}
