using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace ZenithApp.UI.Common;

/// <summary>
/// Обеспечивает плавную интерполяционную прокрутку ScrollViewer и перенаправление скролла от дочерних элементов (nested scroll).
/// </summary>
public static class SmoothScrollHelper
{
    public static readonly DependencyProperty IsSmoothScrollEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsSmoothScrollEnabled",
            typeof(bool),
            typeof(SmoothScrollHelper),
            new PropertyMetadata(false, OnIsSmoothScrollEnabledChanged));

    public static bool GetIsSmoothScrollEnabled(DependencyObject obj) =>
        (bool)obj.GetValue(IsSmoothScrollEnabledProperty);

    public static void SetIsSmoothScrollEnabled(DependencyObject obj, bool value) =>
        obj.SetValue(IsSmoothScrollEnabledProperty, value);

    private static readonly DependencyProperty TargetVerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "TargetVerticalOffset",
            typeof(double),
            typeof(SmoothScrollHelper),
            new PropertyMetadata(0.0));

    private static readonly DependencyProperty CurrentVerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "CurrentVerticalOffset",
            typeof(double),
            typeof(SmoothScrollHelper),
            new PropertyMetadata(0.0, OnCurrentVerticalOffsetChanged));

    private static void OnCurrentVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
        }
    }

    private static void OnIsSmoothScrollEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scrollViewer)
        {
            if ((bool)e.NewValue)
            {
                scrollViewer.PreviewMouseWheel += OnScrollViewerPreviewMouseWheel;
            }
            else
            {
                scrollViewer.PreviewMouseWheel -= OnScrollViewerPreviewMouseWheel;
            }
        }
    }

    private static void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;

        // Если скроллить некуда (контент помещается целиком) — передаем событие дальше
        if (scrollViewer.ScrollableHeight <= 0) return;

        e.Handled = true;

        double currentTarget = (double)scrollViewer.GetValue(TargetVerticalOffsetProperty);
        double currentOffset = scrollViewer.VerticalOffset;

        // Если текущее целевое смещение сильно отстает от текущего реального положения
        if (Math.Abs(currentTarget - currentOffset) > 200)
        {
            currentTarget = currentOffset;
        }

        // Шаг прокрутки за один щелчок колесика (стандартный MouseWheel delta = 120)
        double scrollStep = (e.Delta / 120.0) * 80.0;
        double newTarget = Math.Clamp(currentTarget - scrollStep, 0, scrollViewer.ScrollableHeight);

        scrollViewer.SetValue(TargetVerticalOffsetProperty, newTarget);
        scrollViewer.SetValue(CurrentVerticalOffsetProperty, currentOffset);

        var animation = new DoubleAnimation
        {
            From = currentOffset,
            To = newTarget,
            Duration = TimeSpan.FromMilliseconds(180),
            DecelerationRatio = 0.3,
            AccelerationRatio = 0.1,
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (s, args) =>
        {
            scrollViewer.ScrollToVerticalOffset(newTarget);
            scrollViewer.SetValue(CurrentVerticalOffsetProperty, newTarget);
        };

        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, scrollViewer);
        Storyboard.SetTargetProperty(animation, new PropertyPath(CurrentVerticalOffsetProperty));
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
}
