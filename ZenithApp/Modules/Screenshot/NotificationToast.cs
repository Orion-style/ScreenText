using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace ZenithApp.Modules.Screenshot;

public enum ToastNotificationType
{
    Success,
    Error,
    Info
}

public static class NotificationToast
{
    public const string AppUserModelId = "Zenith.Utility";

    public static void Show(string message, string? title = null, ToastNotificationType type = ToastNotificationType.Success, int durationSeconds = 3)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        // 1. Попытка отправить нативный тост Windows (попадает в системный трей / Центр уведомлений)
        try
        {
            if (TryShowNativeToast(message, title, type, durationSeconds))
            {
                return;
            }
        }
        catch
        {
            // В случае отсутствия поддержки или сбоя переходим к аккуратному запасному WPF тосту
        }

        // 2. Резервный показ элегантного компактного всплывающего окна WPF
        ShowFallbackToast(message, title, type, durationSeconds);
    }

    private static bool TryShowNativeToast(string message, string? title, ToastNotificationType type, int durationSeconds)
    {
        var resolvedTitle = !string.IsNullOrWhiteSpace(title)
            ? title
            : (type switch
            {
                ToastNotificationType.Error => "Zenith — Ошибка",
                ToastNotificationType.Info => "Zenith — Информация",
                _ => "Zenith Utility"
            });

        var safeTitle = SecurityElement.Escape(resolvedTitle);
        var safeMessage = SecurityElement.Escape(message);

        // Иконка приложения для отображения в системном уведомлении
        string iconNode = string.Empty;
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "app.png");
        if (File.Exists(iconPath))
        {
            var uriFormatted = "file:///" + iconPath.Replace('\\', '/');
            iconNode = $"<image placement=\"appLogoOverride\" hint-crop=\"circle\" src=\"{SecurityElement.Escape(uriFormatted)}\"/>";
        }

        var xmlString = $@"
<toast duration=""{(durationSeconds > 5 ? "long" : "short")}"">
    <visual>
        <binding template=""ToastGeneric"">
            <text hint-maxLines=""1"">{safeTitle}</text>
            <text>{safeMessage}</text>
            {iconNode}
        </binding>
    </visual>
</toast>";

        var xmlDoc = new XmlDocument();
        xmlDoc.LoadXml(xmlString);

        var toast = new ToastNotification(xmlDoc)
        {
            ExpirationTime = DateTimeOffset.Now.AddSeconds(Math.Max(5, durationSeconds * 2))
        };

        var notifier = ToastNotificationManager.CreateToastNotifier(AppUserModelId);
        notifier.Show(toast);
        return true;
    }

    private static void ShowFallbackToast(string message, string? title, ToastNotificationType type, int durationSeconds)
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
                Background = new SolidColorBrush(Color.FromArgb(235, 30, 30, 34)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 16, 10),
                Margin = new Thickness(14),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 14,
                    Opacity = 0.35,
                    ShadowDepth = 2,
                    Color = Colors.Black
                }
            };

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconContainer = CreateStatusIcon(type);
            Grid.SetColumn(iconContainer, 0);
            mainGrid.Children.Add(iconContainer);

            var textStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 300,
                Margin = new Thickness(10, 0, 0, 0)
            };

            if (!string.IsNullOrEmpty(title))
            {
                textStack.Children.Add(new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 2),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            textStack.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
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
                window.Left = primaryArea.Right - window.ActualWidth - 12;
                window.Top = primaryArea.Bottom - window.ActualHeight - 12;

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(durationSeconds) };
                timer.Tick += (s, ev) =>
                {
                    timer.Stop();
                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200));
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
            Width = 24,
            Height = 24,
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

                var checkmark = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 7 12 L 10.5 15.5 L 17 8.5"),
                    Stroke = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                    StrokeThickness = 1.8,
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

                var crossGroup = new GeometryGroup();
                crossGroup.Children.Add(new LineGeometry(new Point(7, 7), new Point(17, 17)));
                crossGroup.Children.Add(new LineGeometry(new Point(17, 7), new Point(7, 17)));

                var cross = new System.Windows.Shapes.Path
                {
                    Data = crossGroup,
                    Stroke = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                    StrokeThickness = 1.8,
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
                    FontSize = 12,
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
