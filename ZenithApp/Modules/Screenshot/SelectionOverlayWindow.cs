using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ZenithApp.Modules.Screenshot;

public sealed class SelectionOverlayWindow : Window
{
    private readonly System.Drawing.Rectangle _virtualBounds;
    private readonly Bitmap _screenCapture;
    private readonly Action<System.Drawing.Rectangle?> _onComplete;
    private readonly ScreenshotSettings _settings;
    private readonly ScreenshotMagnifier? _magnifier;

    private readonly Canvas _canvas;
    private readonly System.Windows.Controls.Image _bgImage;
    private readonly System.Windows.Shapes.Path _dimOverlayPath;
    private readonly System.Windows.Shapes.Rectangle _savedRect;
    private readonly System.Windows.Shapes.Rectangle _selectionRect;
    private readonly Border _hintBanner;
    private readonly TextBlock _hintTextBlock;

    private System.Drawing.Rectangle? _activeSavedAreaPx;
    private System.Windows.Point _startPointDips;
    private bool _isSelecting;
    private bool _isMovingSavedRect;
    private System.Windows.Point _moveStartCursorDip;
    private System.Drawing.Rectangle _moveStartRectPx;
    private bool _isMagnifierHiddenByUser;

    // Масштаб DPI между экранными физическими пикселями и WPF DIPs
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    public SelectionOverlayWindow(
        System.Drawing.Rectangle virtualBounds,
        Bitmap screenCapture,
        Action<System.Drawing.Rectangle?> onComplete,
        System.Drawing.Rectangle? previousArea = null,
        ScreenshotSettings? settings = null)
    {
        _virtualBounds = virtualBounds;
        _screenCapture = screenCapture;
        _onComplete = onComplete;
        _settings = settings ?? new ScreenshotSettings();
        _activeSavedAreaPx = ClampSavedAreaToBounds(previousArea, _screenCapture.Width, _screenCapture.Height);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Background = System.Windows.Media.Brushes.Black;

        var rootGrid = new Grid();
        Content = rootGrid;

        _canvas = new Canvas { Background = System.Windows.Media.Brushes.Transparent };
        rootGrid.Children.Add(_canvas);

        _bgImage = new System.Windows.Controls.Image
        {
            Stretch = Stretch.Fill,
            Source = BitmapToImageSource(_screenCapture)
        };
        _canvas.Children.Add(_bgImage);

        // Затемняющий слой с вырезом под активную/сохранённую область
        _dimOverlayPath = new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 0, 0, 0)),
            IsHitTestVisible = false
        };
        _canvas.Children.Add(_dimOverlayPath);

        // Контур сохранённой области (можно перетаскивать без изменения пропорций)
        _savedRect = new System.Windows.Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(240, 0, 200, 255)),
            StrokeThickness = 2,
            Fill = System.Windows.Media.Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(_savedRect);

        // Рамка нового выделения
        _selectionRect = new System.Windows.Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 0, 120, 215)),
            StrokeThickness = 2,
            Fill = System.Windows.Media.Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(_selectionRect);

        // Круглая лупа с пиксельной сеткой (без координат и текста)
        if (_settings.MagnifierEnabled)
        {
            _magnifier = new ScreenshotMagnifier(_settings.MagnifierZoom, _settings.MagnifierGridSize);
            _magnifier.ZoomChanged += zoom => _settings.MagnifierZoom = zoom;
            _magnifier.Visibility = Visibility.Collapsed;
            _canvas.Children.Add(_magnifier);
        }

        // Информационная плашка сверху
        _hintTextBlock = new TextBlock
        {
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.Medium
        };
        _hintBanner = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(210, 20, 20, 20)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16, 7, 16, 7),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 24, 0, 0),
            IsHitTestVisible = false,
            Child = _hintTextBlock
        };
        rootGrid.Children.Add(_hintBanner);
        UpdateHintBannerText();

        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;
    }

    /// <summary>
    /// Проверяет и привязывает сохранённую область к границам экрана, не меняя её ширину и высоту.
    /// </summary>
    public static System.Drawing.Rectangle? ClampSavedAreaToBounds(
        System.Drawing.Rectangle? area,
        int boundsWidth,
        int boundsHeight)
    {
        if (!area.HasValue) return null;
        var rect = area.Value;
        if (rect.Width <= 3 || rect.Height <= 3 || boundsWidth <= 0 || boundsHeight <= 0)
        {
            return null;
        }

        if (rect.Width > boundsWidth || rect.Height > boundsHeight)
        {
            return null;
        }

        int clampedX = Math.Clamp(rect.X, 0, boundsWidth - rect.Width);
        int clampedY = Math.Clamp(rect.Y, 0, boundsHeight - rect.Height);
        return new System.Drawing.Rectangle(clampedX, clampedY, rect.Width, rect.Height);
    }

    /// <summary>
    /// Перемещает область на указанное смещение в пределах экрана, строго сохраняя её размеры и пропорции.
    /// </summary>
    public static System.Drawing.Rectangle MoveAreaPreservingSize(
        System.Drawing.Rectangle startArea,
        int deltaPxX,
        int deltaPxY,
        int boundsWidth,
        int boundsHeight)
    {
        int maxX = Math.Max(0, boundsWidth - startArea.Width);
        int maxY = Math.Max(0, boundsHeight - startArea.Height);
        int newX = Math.Clamp(startArea.X + deltaPxX, 0, maxX);
        int newY = Math.Clamp(startArea.Y + deltaPxY, 0, maxY);
        return new System.Drawing.Rectangle(newX, newY, startArea.Width, startArea.Height);
    }

    private void UpdateHintBannerText()
    {
        _hintTextBlock.Text = _activeSavedAreaPx.HasValue
            ? "Перетащите рамку (пропорции сохраняются) | Enter или 2×клик — снять | Выделите новую снаружи | Esc — отмена"
            : "Выделите область экрана мышью | Колёсико: зум сетки | [M] скрыть сетку | Esc — отмена";
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }

        // Позиционируем окно ровно по границам виртуального экрана в DIPs
        Left = _virtualBounds.X / _dpiScaleX;
        Top = _virtualBounds.Y / _dpiScaleY;
        Width = _virtualBounds.Width / _dpiScaleX;
        Height = _virtualBounds.Height / _dpiScaleY;

        _bgImage.Width = Width;
        _bgImage.Height = Height;

        if (_activeSavedAreaPx.HasValue)
        {
            UpdateSavedRectVisuals();
        }
        else
        {
            UpdateDimCutout(null);
        }

        Focus();
    }

    private void UpdateSavedRectVisuals()
    {
        if (!_activeSavedAreaPx.HasValue)
        {
            _savedRect.Visibility = Visibility.Collapsed;
            UpdateDimCutout(null);
            return;
        }

        var area = _activeSavedAreaPx.Value;
        double leftDip = area.X / _dpiScaleX;
        double topDip = area.Y / _dpiScaleY;
        double widthDip = area.Width / _dpiScaleX;
        double heightDip = area.Height / _dpiScaleY;

        Canvas.SetLeft(_savedRect, leftDip);
        Canvas.SetTop(_savedRect, topDip);
        _savedRect.Width = widthDip;
        _savedRect.Height = heightDip;
        _savedRect.Visibility = Visibility.Visible;

        UpdateDimCutout(new Rect(leftDip, topDip, widthDip, heightDip));
    }

    private void UpdateDimCutout(Rect? cutoutDip)
    {
        var fullRect = new RectangleGeometry(new Rect(0, 0, Math.Max(1, Width), Math.Max(1, Height)));
        if (cutoutDip.HasValue && cutoutDip.Value.Width > 0 && cutoutDip.Value.Height > 0)
        {
            var hole = new RectangleGeometry(cutoutDip.Value);
            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, hole);
            combined.Freeze();
            _dimOverlayPath.Data = combined;
        }
        else
        {
            fullRect.Freeze();
            _dimOverlayPath.Data = fullRect;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelSelection();
            return;
        }

        if ((e.Key == Key.Enter || e.Key == Key.Space) && _activeSavedAreaPx.HasValue && !_isSelecting)
        {
            ConfirmSavedArea();
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.Delete || e.Key == Key.Back) && _activeSavedAreaPx.HasValue && !_isSelecting && !_isMovingSavedRect)
        {
            ClearActiveSavedArea();
            e.Handled = true;
            return;
        }

        if (_activeSavedAreaPx.HasValue && !_isSelecting && !_isMovingSavedRect &&
            e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            int dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
            int dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
            _activeSavedAreaPx = MoveAreaPreservingSize(_activeSavedAreaPx.Value, dx, dy, _screenCapture.Width, _screenCapture.Height);
            UpdateSavedRectVisuals();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.M && _magnifier != null)
        {
            _isMagnifierHiddenByUser = !_isMagnifierHiddenByUser;
            _magnifier.Visibility = _isMagnifierHiddenByUser ? Visibility.Collapsed : Visibility.Visible;
            if (!_isMagnifierHiddenByUser)
            {
                UpdateMagnifierAtCurrentMouse();
            }
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.OemPlus || e.Key == Key.Add) && _magnifier != null)
        {
            _magnifier.AdjustZoom(+1);
            UpdateMagnifierAtCurrentMouse();
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.OemMinus || e.Key == Key.Subtract) && _magnifier != null)
        {
            _magnifier.AdjustZoom(-1);
            UpdateMagnifierAtCurrentMouse();
            e.Handled = true;
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_magnifier != null && !_isMagnifierHiddenByUser)
        {
            _magnifier.AdjustZoom(e.Delta > 0 ? 1 : -1);
            UpdateMagnifierAtCurrentMouse();
            e.Handled = true;
        }
    }

    private void UpdateMagnifierAtCurrentMouse()
    {
        if (_magnifier == null || _isMagnifierHiddenByUser || _isMovingSavedRect) return;

        var currentPoint = Mouse.GetPosition(_canvas);
        if (!_isSelecting && IsPointInsideSavedRect(currentPoint))
        {
            _magnifier.Visibility = Visibility.Collapsed;
            return;
        }

        int pxX = (int)Math.Round(currentPoint.X * _dpiScaleX);
        int pxY = (int)Math.Round(currentPoint.Y * _dpiScaleY);
        if (_magnifier.Visibility != Visibility.Visible)
        {
            _magnifier.Visibility = Visibility.Visible;
        }
        _magnifier.Update(_screenCapture, pxX, pxY, currentPoint, Width, Height);
    }

    private bool IsPointInsideSavedRect(System.Windows.Point pointDip)
    {
        if (!_activeSavedAreaPx.HasValue || _savedRect.Visibility != Visibility.Visible) return false;

        double left = Canvas.GetLeft(_savedRect);
        double top = Canvas.GetTop(_savedRect);
        return pointDip.X >= left && pointDip.X <= left + _savedRect.Width &&
               pointDip.Y >= top && pointDip.Y <= top + _savedRect.Height;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.RightButton == MouseButtonState.Pressed)
        {
            CancelSelection();
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed) return;

        var currentPoint = e.GetPosition(_canvas);

        if (_activeSavedAreaPx.HasValue && IsPointInsideSavedRect(currentPoint))
        {
            if (e.ClickCount >= 2)
            {
                ConfirmSavedArea();
                return;
            }

            _isMovingSavedRect = true;
            _moveStartCursorDip = currentPoint;
            _moveStartRectPx = _activeSavedAreaPx.Value;
            _hintBanner.Visibility = Visibility.Collapsed;
            if (_magnifier != null)
            {
                _magnifier.Visibility = Visibility.Collapsed;
            }
            Cursor = Cursors.SizeAll;
            CaptureMouse();
            return;
        }

        // Начинаем выделение новой области
        _savedRect.Visibility = Visibility.Collapsed;
        _hintBanner.Visibility = Visibility.Collapsed;

        _isSelecting = true;
        _startPointDips = currentPoint;
        Canvas.SetLeft(_selectionRect, _startPointDips.X);
        Canvas.SetTop(_selectionRect, _startPointDips.Y);
        _selectionRect.Width = 0;
        _selectionRect.Height = 0;
        _selectionRect.Visibility = Visibility.Visible;
        UpdateDimCutout(null);
        Cursor = Cursors.Cross;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var currentPoint = e.GetPosition(_canvas);

        if (_isMovingSavedRect)
        {
            int deltaPxX = (int)Math.Round((currentPoint.X - _moveStartCursorDip.X) * _dpiScaleX);
            int deltaPxY = (int)Math.Round((currentPoint.Y - _moveStartCursorDip.Y) * _dpiScaleY);
            _activeSavedAreaPx = MoveAreaPreservingSize(_moveStartRectPx, deltaPxX, deltaPxY, _screenCapture.Width, _screenCapture.Height);
            UpdateSavedRectVisuals();
            return;
        }

        int pxX = (int)Math.Round(currentPoint.X * _dpiScaleX);
        int pxY = (int)Math.Round(currentPoint.Y * _dpiScaleY);

        if (_isSelecting)
        {
            var x = Math.Min(_startPointDips.X, currentPoint.X);
            var y = Math.Min(_startPointDips.Y, currentPoint.Y);
            var w = Math.Abs(currentPoint.X - _startPointDips.X);
            var h = Math.Abs(currentPoint.Y - _startPointDips.Y);

            Canvas.SetLeft(_selectionRect, x);
            Canvas.SetTop(_selectionRect, y);
            _selectionRect.Width = w;
            _selectionRect.Height = h;
            UpdateDimCutout(new Rect(x, y, w, h));

            if (_magnifier != null && !_isMagnifierHiddenByUser)
            {
                if (_magnifier.Visibility != Visibility.Visible)
                {
                    _magnifier.Visibility = Visibility.Visible;
                }
                _magnifier.Update(_screenCapture, pxX, pxY, currentPoint, Width, Height);
            }
            return;
        }

        // Режим наведения без зажатой кнопки
        if (IsPointInsideSavedRect(currentPoint))
        {
            Cursor = Cursors.SizeAll;
            if (_magnifier != null)
            {
                _magnifier.Visibility = Visibility.Collapsed;
            }
            return;
        }

        Cursor = Cursors.Cross;
        if (_magnifier != null && !_isMagnifierHiddenByUser)
        {
            if (_magnifier.Visibility != Visibility.Visible)
            {
                _magnifier.Visibility = Visibility.Visible;
            }
            _magnifier.Update(_screenCapture, pxX, pxY, currentPoint, Width, Height);
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isMovingSavedRect)
        {
            _isMovingSavedRect = false;
            ReleaseMouseCapture();
            UpdateSavedRectVisuals();
            _hintBanner.Visibility = Visibility.Visible;
            return;
        }

        if (!_isSelecting) return;
        _isSelecting = false;
        ReleaseMouseCapture();

        if (_magnifier != null)
        {
            _magnifier.Visibility = Visibility.Collapsed;
        }

        var currentPoint = e.GetPosition(_canvas);
        var leftDip = Math.Min(_startPointDips.X, currentPoint.X);
        var topDip = Math.Min(_startPointDips.Y, currentPoint.Y);
        var widthDip = Math.Abs(currentPoint.X - _startPointDips.X);
        var heightDip = Math.Abs(currentPoint.Y - _startPointDips.Y);

        int pxX = Math.Clamp((int)Math.Round(leftDip * _dpiScaleX), 0, Math.Max(0, _screenCapture.Width - 1));
        int pxY = Math.Clamp((int)Math.Round(topDip * _dpiScaleY), 0, Math.Max(0, _screenCapture.Height - 1));
        int pxW = Math.Min((int)Math.Round(widthDip * _dpiScaleX), _screenCapture.Width - pxX);
        int pxH = Math.Min((int)Math.Round(heightDip * _dpiScaleY), _screenCapture.Height - pxY);

        if (pxW > 3 && pxH > 3)
        {
            Close();
            _onComplete(new System.Drawing.Rectangle(pxX, pxY, pxW, pxH));
            return;
        }

        // Если был случайный одиночный клик при активной сохранённой рамке — возвращаем рамку вместо закрытия
        _selectionRect.Visibility = Visibility.Collapsed;
        if (_activeSavedAreaPx.HasValue)
        {
            UpdateSavedRectVisuals();
            _hintBanner.Visibility = Visibility.Visible;
        }
        else
        {
            Close();
            _onComplete(null);
        }
    }

    private void ConfirmSavedArea()
    {
        if (!_activeSavedAreaPx.HasValue) return;
        var result = _activeSavedAreaPx.Value;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
        Close();
        _onComplete(result);
    }

    private void ClearActiveSavedArea()
    {
        _activeSavedAreaPx = null;
        UpdateSavedRectVisuals();
        UpdateHintBannerText();
        _hintBanner.Visibility = Visibility.Visible;
        Cursor = Cursors.Cross;
    }

    private void CancelSelection()
    {
        if (_isSelecting || _isMovingSavedRect)
        {
            _isSelecting = false;
            _isMovingSavedRect = false;
            ReleaseMouseCapture();
        }
        Close();
        _onComplete(null);
    }

    private static BitmapSource BitmapToImageSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
