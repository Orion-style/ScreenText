using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ZenithApp.Modules.NextSession;

public enum NextSessionCountdownOutcome
{
    Completed,
    RunNow,
    Cancelled
}

public partial class NextSessionCountdownOverlay : Window
{
    private readonly int _totalSeconds;
    private readonly int _programCount;
    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _timer;
    private readonly TaskCompletionSource<NextSessionCountdownOutcome> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isClosing;

    public NextSessionCountdownOverlay(int totalSeconds, int programCount, string? messagePreview)
    {
        InitializeComponent();

        _totalSeconds = Math.Max(1, totalSeconds);
        _programCount = Math.Max(0, programCount);

        if (!string.IsNullOrWhiteSpace(messagePreview))
        {
            TxtMessagePreview.Text = $"«{messagePreview.Trim()}»";
            TxtMessagePreview.Visibility = Visibility.Visible;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _timer.Tick += OnTimerTick;

        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _timer.Stop();
            _tcs.TrySetResult(NextSessionCountdownOutcome.Cancelled);
        };
    }

    public Task<NextSessionCountdownOutcome> WaitForCompletionAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                Dispatcher.BeginInvoke(() => FinishAndClose(NextSessionCountdownOutcome.Cancelled));
            });
        }

        return _tcs.Task;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionAtBottomRight();
        UpdateVisualProgress(0);

        _stopwatch.Start();
        _timer.Start();

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220));
        BeginAnimation(OpacityProperty, fadeIn);
    }

    private void PositionAtBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 16;
        Top = workArea.Bottom - ActualHeight - 16;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var totalMs = _totalSeconds * 1000.0;
        var elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;

        if (elapsedMs >= totalMs)
        {
            UpdateVisualProgress(1.0);
            FinishAndClose(NextSessionCountdownOutcome.Completed);
            return;
        }

        UpdateVisualProgress(elapsedMs / totalMs);
    }

    private void UpdateVisualProgress(double fraction)
    {
        var clamped = Math.Clamp(fraction, 0.0, 1.0);
        var remainingSeconds = Math.Max(0, (int)Math.Ceiling(_totalSeconds * (1.0 - clamped)));

        TxtSecondsLeft.Text = remainingSeconds.ToString();

        var suffix = _programCount > 0
            ? $" • Программ: {_programCount}"
            : " • Уведомление";
        TxtCountdownDetail.Text = $"Запуск через {remainingSeconds} сек{suffix}";

        var barWidth = Math.Max(0, CardBorder.ActualWidth > 0 ? CardBorder.ActualWidth : 326);
        ProgressFill.Width = barWidth * (1.0 - clamped);
    }

    private void OnRunNowClicked(object sender, RoutedEventArgs e)
    {
        FinishAndClose(NextSessionCountdownOutcome.RunNow);
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        FinishAndClose(NextSessionCountdownOutcome.Cancelled);
    }

    private void FinishAndClose(NextSessionCountdownOutcome outcome)
    {
        if (_isClosing) return;
        _isClosing = true;

        _timer.Stop();
        _stopwatch.Stop();
        _tcs.TrySetResult(outcome);

        var fadeOut = new DoubleAnimation(Opacity, 0.0, TimeSpan.FromMilliseconds(180));
        fadeOut.Completed += (_, _) =>
        {
            try
            {
                Close();
            }
            catch
            {
                // Ignore if already closed
            }
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }
}
