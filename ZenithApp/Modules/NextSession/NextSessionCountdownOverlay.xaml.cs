using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
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
    private const double RingCenter = 30.0;
    private const double RingRadius = 26.0;

    private readonly int _totalSeconds;
    private readonly IReadOnlyList<string> _programNames;
    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _timer;
    private readonly TaskCompletionSource<NextSessionCountdownOutcome> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenRegistration _cancelRegistration;
    private bool _isClosing;

    public NextSessionCountdownOverlay(int totalSeconds, int programCount, string? messagePreview)
        : this(
            totalSeconds,
            programCount > 0
                ? Enumerable.Range(1, programCount).Select(i => $"Приложение {i}").ToList()
                : Array.Empty<string>(),
            messagePreview)
    {
    }

    public NextSessionCountdownOverlay(int totalSeconds, IReadOnlyList<string> programNames, string? messagePreview)
    {
        InitializeComponent();

        _totalSeconds = Math.Max(1, totalSeconds);
        _programNames = (programNames ?? Array.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToList();

        TxtProgramsSummary.Text = BuildProgramsSummary(_programNames);

        if (!string.IsNullOrWhiteSpace(messagePreview))
        {
            TxtMessagePreview.Text = $"«{messagePreview.Trim()}»";
            MessagePreviewBorder.Visibility = Visibility.Visible;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _timer.Tick += OnTimerTick;

        Loaded += OnLoaded;
        SizeChanged += (_, _) => PositionAtBottomRight();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _cancelRegistration.Dispose();
            _tcs.TrySetResult(NextSessionCountdownOutcome.Cancelled);
        };
    }

    public Task<NextSessionCountdownOutcome> WaitForCompletionAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            _cancelRegistration = cancellationToken.Register(() =>
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
        if (!IsLoaded) return;
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
        var remainingFraction = 1.0 - clamped;
        var remainingSeconds = Math.Max(0, (int)Math.Ceiling(_totalSeconds * remainingFraction));

        TxtSecondsLeft.Text = remainingSeconds.ToString();
        TxtCountdownTitle.Text = _programNames.Count > 0
            ? $"Запуск через {remainingSeconds} сек"
            : $"Напоминание через {remainingSeconds} сек";

        ProgressArcPath.Data = BuildArcGeometry(remainingFraction);

        var innerWidth = CardBorder.ActualWidth > 2 ? CardBorder.ActualWidth - 2 : 358;
        ProgressFill.Width = Math.Max(0, innerWidth * remainingFraction);
    }

    private static Geometry? BuildArcGeometry(double remainingFraction)
    {
        if (remainingFraction <= 0.002)
        {
            return null;
        }

        if (remainingFraction >= 0.999)
        {
            return new EllipseGeometry(new Point(RingCenter, RingCenter), RingRadius, RingRadius);
        }

        var angleDegrees = remainingFraction * 360.0;
        var startPoint = new Point(RingCenter, RingCenter - RingRadius);
        var endAngleRad = (angleDegrees - 90.0) * Math.PI / 180.0;
        var endPoint = new Point(
            RingCenter + RingRadius * Math.Cos(endAngleRad),
            RingCenter + RingRadius * Math.Sin(endAngleRad));

        var segment = new ArcSegment
        {
            Point = endPoint,
            Size = new Size(RingRadius, RingRadius),
            IsLargeArc = angleDegrees > 180.0,
            SweepDirection = SweepDirection.Clockwise,
            IsStroked = true
        };

        var figure = new PathFigure
        {
            StartPoint = startPoint,
            IsClosed = false,
            Segments = { segment }
        };

        return new PathGeometry { Figures = { figure } };
    }

    private static string BuildProgramsSummary(IReadOnlyList<string> programNames)
    {
        if (programNames.Count == 0)
        {
            return "Показ сохранённой заметки для сеанса";
        }

        if (programNames.Count == 1)
        {
            return $"Приложение: {programNames[0]}";
        }

        if (programNames.Count <= 3)
        {
            return $"Программы ({programNames.Count}): {string.Join(", ", programNames)}";
        }

        return $"Программы ({programNames.Count}): {programNames[0]}, {programNames[1]} (+{programNames.Count - 2})";
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
        _cancelRegistration.Dispose();
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
