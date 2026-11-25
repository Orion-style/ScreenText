using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ZenithApp.Modules.Screenshot;

/// <summary>
/// Чистая круглая лупа с сеткой пикселей для точного позиционирования курсора при выборе области.
/// Поддерживает масштабирование колёсиком мыши или клавишами +/-.
/// </summary>
public sealed class ScreenshotMagnifier : Border
{
    private readonly PixelGridElement _pixelGridElement;

    private int _zoom;
    private int _gridSize;

    public event Action<int>? ZoomChanged;

    public int Zoom
    {
        get => _zoom;
        set
        {
            int clamped = Math.Clamp(value, 2, 24);
            if (_zoom != clamped)
            {
                _zoom = clamped;
                _pixelGridElement.UpdateSettings(_gridSize, _zoom);
                ZoomChanged?.Invoke(_zoom);
            }
        }
    }

    public int GridSize
    {
        get => _gridSize;
        set
        {
            int odd = Math.Clamp(value % 2 == 0 ? value + 1 : value, 5, 25);
            if (_gridSize != odd)
            {
                _gridSize = odd;
                _pixelGridElement.UpdateSettings(_gridSize, _zoom);
            }
        }
    }

    public ScreenshotMagnifier(int initialZoom = 8, int initialGridSize = 11)
    {
        _zoom = Math.Clamp(initialZoom, 2, 24);
        _gridSize = Math.Clamp(initialGridSize % 2 == 0 ? initialGridSize + 1 : initialGridSize, 5, 25);

        IsHitTestVisible = false;
        Background = System.Windows.Media.Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        Effect = new DropShadowEffect
        {
            BlurRadius = 12,
            ShadowDepth = 2,
            Opacity = 0.55,
            Color = System.Windows.Media.Colors.Black
        };

        _pixelGridElement = new PixelGridElement(_gridSize, _zoom);
        Child = _pixelGridElement;
    }

    public void AdjustZoom(int delta)
    {
        Zoom += delta;
    }

    /// <summary>
    /// Обновляет выборку пикселей сетки и позиционирует круглую лупу рядом с курсором.
    /// </summary>
    public void Update(
        System.Drawing.Bitmap screenBmp,
        int cursorPxX,
        int cursorPxY,
        System.Windows.Point cursorDip,
        double overlayWidthDip,
        double overlayHeightDip)
    {
        _pixelGridElement.SampleFromBitmap(screenBmp, cursorPxX, cursorPxY);
        UpdatePosition(cursorDip, overlayWidthDip, overlayHeightDip);
    }

    private void UpdatePosition(System.Windows.Point cursorDip, double overlayWidthDip, double overlayHeightDip)
    {
        Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = DesiredSize.Width > 0 ? DesiredSize.Width : 110;
        double height = DesiredSize.Height > 0 ? DesiredSize.Height : 110;

        const double offset = 20;

        double left = cursorDip.X + offset;
        double top = cursorDip.Y + offset;

        if (left + width > overlayWidthDip - 10)
        {
            left = cursorDip.X - width - offset;
        }

        if (top + height > overlayHeightDip - 10)
        {
            top = cursorDip.Y - height - offset;
        }

        left = Math.Max(10, Math.Min(left, overlayWidthDip - width - 10));
        top = Math.Max(10, Math.Min(top, overlayHeightDip - height - 10));

        Canvas.SetLeft(this, left);
        Canvas.SetTop(this, top);
    }

    /// <summary>
    /// Круглый элемент отрисовки увеличенной сетки пикселей с перекрестием центрального пикселя.
    /// </summary>
    private sealed class PixelGridElement : FrameworkElement
    {
        private int _gridSize;
        private int _zoom;
        private readonly System.Windows.Media.Color[,] _pixels;

        private readonly Pen _gridPen;
        private readonly Pen _centerOuterPen;
        private readonly Pen _centerInnerPen;
        private readonly Pen _crosshairPen;
        private readonly Pen _outerDarkRingPen;
        private readonly Pen _outerLightRingPen;

        public PixelGridElement(int gridSize, int zoom)
        {
            _gridSize = gridSize;
            _zoom = zoom;
            _pixels = new System.Windows.Media.Color[25, 25];

            _gridPen = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, 255, 255, 255)), 0.6);
            _gridPen.Freeze();

            _centerOuterPen = new Pen(System.Windows.Media.Brushes.Black, 2.2);
            _centerOuterPen.Freeze();

            _centerInnerPen = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 60, 60)), 1.2);
            _centerInnerPen.Freeze();

            _crosshairPen = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(145, 255, 60, 60)), 1.0);
            _crosshairPen.Freeze();

            _outerDarkRingPen = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(190, 15, 15, 18)), 3.0);
            _outerDarkRingPen.Freeze();

            _outerLightRingPen = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 255, 255, 255)), 1.4);
            _outerLightRingPen.Freeze();

            UpdateSettings(gridSize, zoom);
        }

        public void UpdateSettings(int gridSize, int zoom)
        {
            _gridSize = gridSize;
            _zoom = zoom;
            double cellSize = GetCellSize();
            double diameter = _gridSize * cellSize;
            Width = diameter;
            Height = diameter;
            InvalidateMeasure();
            InvalidateVisual();
        }

        private double GetCellSize() => Math.Max(4, _zoom + 2);

        public void SampleFromBitmap(System.Drawing.Bitmap bmp, int centerX, int centerY)
        {
            int radius = _gridSize / 2;
            int maxW = bmp.Width - 1;
            int maxH = bmp.Height - 1;

            for (int dy = -radius; dy <= radius; dy++)
            {
                int sampleY = Math.Clamp(centerY + dy, 0, maxH);
                int gridY = dy + radius;

                for (int dx = -radius; dx <= radius; dx++)
                {
                    int sampleX = Math.Clamp(centerX + dx, 0, maxW);
                    int gridX = dx + radius;

                    var gdiColor = bmp.GetPixel(sampleX, sampleY);
                    _pixels[gridX, gridY] = System.Windows.Media.Color.FromRgb(gdiColor.R, gdiColor.G, gdiColor.B);
                }
            }

            InvalidateVisual();
        }

        protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
        {
            double diameter = _gridSize * GetCellSize();
            return new System.Windows.Size(diameter, diameter);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double cellSize = GetCellSize();
            int radius = _gridSize / 2;
            double diameter = _gridSize * cellSize;
            double half = diameter / 2.0;
            var center = new System.Windows.Point(half, half);

            // Круглая маска обрезки для сетки пикселей
            var clipGeometry = new EllipseGeometry(center, half - 1.5, half - 1.5);
            clipGeometry.Freeze();
            dc.PushClip(clipGeometry);

            // 1. Отрисовка пикселей и линий сетки
            for (int y = 0; y < _gridSize; y++)
            {
                for (int x = 0; x < _gridSize; x++)
                {
                    var rect = new Rect(x * cellSize, y * cellSize, cellSize, cellSize);
                    var brush = new SolidColorBrush(_pixels[x, y]);
                    brush.Freeze();
                    dc.DrawRectangle(brush, null, rect);
                    dc.DrawRectangle(null, _gridPen, rect);
                }
            }

            // 2. Перекрестие к центральному пикселю
            double centerLeft = radius * cellSize;
            double centerTop = radius * cellSize;
            double midX = centerLeft + cellSize / 2.0;
            double midY = centerTop + cellSize / 2.0;

            dc.DrawLine(_crosshairPen, new System.Windows.Point(midX, 0), new System.Windows.Point(midX, centerTop));
            dc.DrawLine(_crosshairPen, new System.Windows.Point(midX, centerTop + cellSize), new System.Windows.Point(midX, diameter));
            dc.DrawLine(_crosshairPen, new System.Windows.Point(0, midY), new System.Windows.Point(centerLeft, midY));
            dc.DrawLine(_crosshairPen, new System.Windows.Point(centerLeft + cellSize, midY), new System.Windows.Point(diameter, midY));

            // 3. Рамка центрального пикселя
            var centerRect = new Rect(centerLeft, centerTop, cellSize, cellSize);
            dc.DrawRectangle(null, _centerOuterPen, centerRect);
            dc.DrawRectangle(null, _centerInnerPen, centerRect);

            dc.Pop();

            // 4. Аккуратная круглая окантовка по краю
            double ringRadius = Math.Max(1.0, half - 1.5);
            dc.DrawEllipse(null, _outerDarkRingPen, center, ringRadius, ringRadius);
            dc.DrawEllipse(null, _outerLightRingPen, center, ringRadius, ringRadius);
        }
    }
}
