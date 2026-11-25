using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ZenithApp.Modules.Screenshot;

/// <summary>
/// Плавающая лупа с сеткой пикселей в стиле ShareX для точного позиционирования и выделения области экрана.
/// Отображает увеличенные пиксели, перекрестие центрального пикселя, HEX/RGB цвет и координаты.
/// Поддерживает масштабирование колесиком мыши или клавишами +/-.
/// </summary>
public sealed class ScreenshotMagnifier : Border
{
    private readonly PixelGridElement _pixelGridElement;
    private readonly Border _colorSwatch;
    private readonly TextBlock _hexTextBlock;
    private readonly TextBlock _rgbTextBlock;
    private readonly TextBlock _coordsTextBlock;
    private readonly TextBlock _selectionSizeTextBlock;
    private readonly TextBlock _zoomHintTextBlock;

    private int _zoom;
    private int _gridSize;
    private bool _showColorInfo;
    private System.Drawing.Color _currentPixelColor = System.Drawing.Color.Black;

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
                UpdateZoomHint();
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

    public bool ShowColorInfo
    {
        get => _showColorInfo;
        set
        {
            _showColorInfo = value;
            _colorSwatch.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _hexTextBlock.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _rgbTextBlock.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public ScreenshotMagnifier(int initialZoom = 8, int initialGridSize = 11, bool showColorInfo = true)
    {
        _zoom = Math.Clamp(initialZoom, 2, 24);
        _gridSize = Math.Clamp(initialGridSize % 2 == 0 ? initialGridSize + 1 : initialGridSize, 5, 25);
        _showColorInfo = showColorInfo;

        IsHitTestVisible = false;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(235, 20, 20, 24));
        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 255, 255, 255));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(6);
        Effect = new DropShadowEffect
        {
            BlurRadius = 14,
            ShadowDepth = 2,
            Opacity = 0.5,
            Color = System.Windows.Media.Colors.Black
        };

        var mainStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // 1. Сетка пикселей
        _pixelGridElement = new PixelGridElement(_gridSize, _zoom);
        var gridContainer = new Border
        {
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = _pixelGridElement,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };
        mainStack.Children.Add(gridContainer);

        // 2. Строка с цветом (HEX / RGB)
        var colorRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        };

        _colorSwatch = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(2),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = System.Windows.Media.Brushes.Black
        };
        colorRow.Children.Add(_colorSwatch);

        _hexTextBlock = new TextBlock
        {
            Text = "#000000",
            Foreground = System.Windows.Media.Brushes.White,
            FontFamily = new System.Windows.Media.FontFamily("Consolas, Segoe UI"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        colorRow.Children.Add(_hexTextBlock);

        _rgbTextBlock = new TextBlock
        {
            Text = "(0, 0, 0)",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 200, 200, 200)),
            FontFamily = new System.Windows.Media.FontFamily("Consolas, Segoe UI"),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        colorRow.Children.Add(_rgbTextBlock);

        mainStack.Children.Add(colorRow);

        // 3. Строка с координатами X/Y и размером выделения
        var coordsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 3)
        };

        _coordsTextBlock = new TextBlock
        {
            Text = "X: 0  Y: 0",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 220, 220, 220)),
            FontFamily = new System.Windows.Media.FontFamily("Consolas, Segoe UI"),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        coordsRow.Children.Add(_coordsTextBlock);

        _selectionSizeTextBlock = new TextBlock
        {
            Text = "",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 0, 210, 255)),
            FontFamily = new System.Windows.Media.FontFamily("Consolas, Segoe UI"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        coordsRow.Children.Add(_selectionSizeTextBlock);

        mainStack.Children.Add(coordsRow);

        // 4. Подсказка по масштабу и управлению
        _zoomHintTextBlock = new TextBlock
        {
            Text = $"{_zoom}x (Колёсико / + -)",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(140, 180, 180, 180)),
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        mainStack.Children.Add(_zoomHintTextBlock);

        Child = mainStack;
        ShowColorInfo = showColorInfo;
    }

    public void AdjustZoom(int delta)
    {
        Zoom += delta;
    }

    private void UpdateZoomHint()
    {
        _zoomHintTextBlock.Text = $"{_zoom}x (Колёсико / + -)";
    }

    /// <summary>
    /// Обновляет пиксели сетки, цвет, координаты и позиционирует лупу рядом с курсором.
    /// </summary>
    public void Update(
        System.Drawing.Bitmap screenBmp,
        int cursorPxX,
        int cursorPxY,
        System.Windows.Point cursorDip,
        double overlayWidthDip,
        double overlayHeightDip,
        int? selectionWidthPx = null,
        int? selectionHeightPx = null)
    {
        // 1. Захват пикселей в сетку
        _pixelGridElement.SampleFromBitmap(screenBmp, cursorPxX, cursorPxY);

        // 2. Текущий цвет центрального пикселя
        int clampedX = Math.Clamp(cursorPxX, 0, screenBmp.Width - 1);
        int clampedY = Math.Clamp(cursorPxY, 0, screenBmp.Height - 1);
        _currentPixelColor = screenBmp.GetPixel(clampedX, clampedY);

        var wpfColor = System.Windows.Media.Color.FromRgb(_currentPixelColor.R, _currentPixelColor.G, _currentPixelColor.B);
        _colorSwatch.Background = new SolidColorBrush(wpfColor);
        _hexTextBlock.Text = $"#{_currentPixelColor.R:X2}{_currentPixelColor.G:X2}{_currentPixelColor.B:X2}";
        _rgbTextBlock.Text = $"({_currentPixelColor.R}, {_currentPixelColor.G}, {_currentPixelColor.B})";

        // 3. Координаты
        _coordsTextBlock.Text = $"X: {cursorPxX}  Y: {cursorPxY}";

        // 4. Размер выделения
        if (selectionWidthPx.HasValue && selectionHeightPx.HasValue && (selectionWidthPx.Value > 0 || selectionHeightPx.Value > 0))
        {
            _selectionSizeTextBlock.Text = $"[{selectionWidthPx.Value}×{selectionHeightPx.Value}]";
            _selectionSizeTextBlock.Visibility = Visibility.Visible;
        }
        else
        {
            _selectionSizeTextBlock.Visibility = Visibility.Collapsed;
        }

        // 5. Позиционирование рядом с курсором
        UpdatePosition(cursorDip, overlayWidthDip, overlayHeightDip);
    }

    private void UpdatePosition(System.Windows.Point cursorDip, double overlayWidthDip, double overlayHeightDip)
    {
        Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = DesiredSize.Width > 0 ? DesiredSize.Width : 130;
        double height = DesiredSize.Height > 0 ? DesiredSize.Height : 160;

        const double offset = 20;

        // По умолчанию размещаем справа снизу от курсора
        double left = cursorDip.X + offset;
        double top = cursorDip.Y + offset;

        // Если не помещается справа, отражаем влево
        if (left + width > overlayWidthDip - 10)
        {
            left = cursorDip.X - width - offset;
        }

        // Если не помещается снизу, отражаем вверх
        if (top + height > overlayHeightDip - 10)
        {
            top = cursorDip.Y - height - offset;
        }

        // Ограничиваем окно рамками экрана
        left = Math.Max(10, Math.Min(left, overlayWidthDip - width - 10));
        top = Math.Max(10, Math.Min(top, overlayHeightDip - height - 10));

        Canvas.SetLeft(this, left);
        Canvas.SetTop(this, top);
    }

    public void CopyColorToClipboard()
    {
        try
        {
            string hex = $"#{_currentPixelColor.R:X2}{_currentPixelColor.G:X2}{_currentPixelColor.B:X2}";
            Clipboard.SetText(hex);
        }
        catch
        {
            // Игнорируем блокировки буфера обмена
        }
    }

    /// <summary>
    /// Легковесный элемент отрисовки сетки пикселей с перекрестием и увеличением.
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

        public PixelGridElement(int gridSize, int zoom)
        {
            _gridSize = gridSize;
            _zoom = zoom;
            _pixels = new System.Windows.Media.Color[25, 25];

            _gridPen = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(45, 255, 255, 255)), 0.6);
            _gridPen.Freeze();

            _centerOuterPen = new Pen(System.Windows.Media.Brushes.Black, 2.4);
            _centerOuterPen.Freeze();

            _centerInnerPen = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 60, 60)), 1.4);
            _centerInnerPen.Freeze();

            _crosshairPen = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 255, 60, 60)), 1.0);
            _crosshairPen.Freeze();

            UpdateSettings(gridSize, zoom);
        }

        public void UpdateSettings(int gridSize, int zoom)
        {
            _gridSize = gridSize;
            _zoom = zoom;
            double cellSize = GetCellSize();
            Width = _gridSize * cellSize;
            Height = _gridSize * cellSize;
            InvalidateMeasure();
            InvalidateVisual();
        }

        private double GetCellSize()
        {
            // Размер одной ячейки пикселя в DIPs пропорционально зуму
            return Math.Max(4, _zoom + 2);
        }

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
            double cellSize = GetCellSize();
            return new System.Windows.Size(_gridSize * cellSize, _gridSize * cellSize);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double cellSize = GetCellSize();
            int radius = _gridSize / 2;

            // 1. Отрисовка каждого пикселя из выборки
            for (int y = 0; y < _gridSize; y++)
            {
                for (int x = 0; x < _gridSize; x++)
                {
                    var rect = new Rect(x * cellSize, y * cellSize, cellSize, cellSize);
                    var brush = new SolidColorBrush(_pixels[x, y]);
                    dc.DrawRectangle(brush, null, rect);

                    // Сетка между пикселями
                    dc.DrawRectangle(null, _gridPen, rect);
                }
            }

            // 2. Линии перекрестия от краев к центру (ShareX-стиль)
            double totalWidth = _gridSize * cellSize;
            double totalHeight = _gridSize * cellSize;
            double centerLeft = radius * cellSize;
            double centerTop = radius * cellSize;

            // Верхняя и нижняя засечки
            dc.DrawLine(_crosshairPen, new System.Windows.Point(centerLeft + cellSize / 2, 0), new System.Windows.Point(centerLeft + cellSize / 2, centerTop));
            dc.DrawLine(_crosshairPen, new System.Windows.Point(centerLeft + cellSize / 2, centerTop + cellSize), new System.Windows.Point(centerLeft + cellSize / 2, totalHeight));

            // Левая и правая засечки
            dc.DrawLine(_crosshairPen, new System.Windows.Point(0, centerTop + cellSize / 2), new System.Windows.Point(centerLeft, centerTop + cellSize / 2));
            dc.DrawLine(_crosshairPen, new System.Windows.Point(centerLeft + cellSize, centerTop + cellSize / 2), new System.Windows.Point(totalWidth, centerTop + cellSize / 2));

            // 3. Выделение центрального пикселя (двойная контрастная рамка: внешняя черная, внутренняя красная)
            var centerRect = new Rect(centerLeft, centerTop, cellSize, cellSize);
            dc.DrawRectangle(null, _centerOuterPen, centerRect);
            dc.DrawRectangle(null, _centerInnerPen, centerRect);
        }
    }
}
