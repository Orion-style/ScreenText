using System.Drawing;
using System.IO;
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
    private readonly System.Drawing.Rectangle? _previousArea;
    private readonly ScreenshotSettings _settings;
    private readonly ScreenshotMagnifier? _magnifier;

    private System.Windows.Point _startPointDips;
    private bool _isSelecting;
    private readonly Canvas _canvas;
    private readonly System.Windows.Shapes.Rectangle _selectionRect;
    private readonly System.Windows.Shapes.Rectangle? _previousRect;
    private readonly Border _hintBanner;
    private readonly System.Windows.Controls.Image _bgImage;
    private readonly System.Windows.Shapes.Rectangle _dimOverlay;

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
        _previousArea = previousArea;
        _settings = settings ?? new ScreenshotSettings();

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

        // Затемняющий слой поверх фонового снимка
        _dimOverlay = new System.Windows.Shapes.Rectangle
        {
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 0, 0, 0))
        };
        _canvas.Children.Add(_dimOverlay);

        // Контур предыдущей сохранённой области (если есть)
        if (_previousArea.HasValue && _previousArea.Value.Width > 0 && _previousArea.Value.Height > 0)
        {
            _previousRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 0, 200, 255)),
                StrokeDashArray = new DoubleCollection { 4, 2 },
                StrokeThickness = 2,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 0, 200, 255)),
                Visibility = Visibility.Visible
            };
            _canvas.Children.Add(_previousRect);
        }

        // Рамка нового выделения
        _selectionRect = new System.Windows.Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 0, 120, 215)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 0, 120, 215)),
            Visibility = Visibility.Collapsed
        };
        _canvas.Children.Add(_selectionRect);

        // Лупа с пиксельной сеткой (ShareX-стиль)
        if (_settings.MagnifierEnabled)
        {
            _magnifier = new ScreenshotMagnifier(_settings.MagnifierZoom, _settings.MagnifierGridSize, _settings.MagnifierShowColorInfo);
            _magnifier.ZoomChanged += zoom =>
            {
                _settings.MagnifierZoom = zoom;
            };
            _magnifier.Visibility = Visibility.Collapsed;
            _canvas.Children.Add(_magnifier);
        }

        // Информационная плашка сверху
        var hintText = _previousArea.HasValue && _previousArea.Value.Width > 0 && _previousArea.Value.Height > 0
            ? $"Предыдущая область: {_previousArea.Value.Width}×{_previousArea.Value.Height} (Enter — подтвердить) | Выделите новую мышью | Колёсико: зум сетки | Esc — отмена"
            : "Выделите область экрана мышью | Колёсико: зум сетки пикселей | [M] скрыть сетку | Esc — отмена";

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
            Child = new TextBlock
            {
                Text = hintText,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.Medium
            }
        };
        rootGrid.Children.Add(_hintBanner);

        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;
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
        _dimOverlay.Width = Width;
        _dimOverlay.Height = Height;

        if (_previousRect != null && _previousArea.HasValue)
        {
            Canvas.SetLeft(_previousRect, _previousArea.Value.X / _dpiScaleX);
            Canvas.SetTop(_previousRect, _previousArea.Value.Y / _dpiScaleY);
            _previousRect.Width = _previousArea.Value.Width / _dpiScaleX;
            _previousRect.Height = _previousArea.Value.Height / _dpiScaleY;
        }

        Focus();
        CaptureMouse();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelSelection();
            return;
        }

        if ((e.Key == Key.Enter || e.Key == Key.Space) && _previousArea.HasValue)
        {
            Close();
            _onComplete(_previousArea.Value);
            return;
        }

        if (e.Key == Key.M && _magnifier != null)
        {
            _magnifier.Visibility = _magnifier.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C && _magnifier != null && _magnifier.Visibility == Visibility.Visible)
        {
            _magnifier.CopyColorToClipboard();
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
            return;
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_magnifier != null && _magnifier.Visibility == Visibility.Visible)
        {
            _magnifier.AdjustZoom(e.Delta > 0 ? 1 : -1);
            UpdateMagnifierAtCurrentMouse();
            e.Handled = true;
        }
    }

    private void UpdateMagnifierAtCurrentMouse()
    {
        if (_magnifier == null || _magnifier.Visibility != Visibility.Visible) return;

        var currentPoint = Mouse.GetPosition(_canvas);
        int pxX = (int)Math.Round(currentPoint.X * _dpiScaleX);
        int pxY = (int)Math.Round(currentPoint.Y * _dpiScaleY);

        int? selW = null;
        int? selH = null;
        if (_isSelecting)
        {
            selW = (int)Math.Round(Math.Abs(currentPoint.X - _startPointDips.X) * _dpiScaleX);
            selH = (int)Math.Round(Math.Abs(currentPoint.Y - _startPointDips.Y) * _dpiScaleY);
        }

        _magnifier.Update(_screenCapture, pxX, pxY, currentPoint, Width, Height, selW, selH);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.RightButton == MouseButtonState.Pressed)
        {
            CancelSelection();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (_previousRect != null)
            {
                _previousRect.Visibility = Visibility.Collapsed;
            }
            if (_hintBanner != null)
            {
                _hintBanner.Visibility = Visibility.Collapsed;
            }

            _isSelecting = true;
            _startPointDips = e.GetPosition(_canvas);
            Canvas.SetLeft(_selectionRect, _startPointDips.X);
            Canvas.SetTop(_selectionRect, _startPointDips.Y);
            _selectionRect.Width = 0;
            _selectionRect.Height = 0;
            _selectionRect.Visibility = Visibility.Visible;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var currentPoint = e.GetPosition(_canvas);
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

            if (_magnifier != null && _magnifier.Visibility == Visibility.Visible)
            {
                int selPxW = (int)Math.Round(w * _dpiScaleX);
                int selPxH = (int)Math.Round(h * _dpiScaleY);
                _magnifier.Update(_screenCapture, pxX, pxY, currentPoint, Width, Height, selPxW, selPxH);
            }
        }
        else
        {
            if (_magnifier != null)
            {
                if (_magnifier.Visibility != Visibility.Visible)
                {
                    _magnifier.Visibility = Visibility.Visible;
                }
                _magnifier.Update(_screenCapture, pxX, pxY, currentPoint, Width, Height);
            }
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
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

        // Переводим из DIPs координат оверлея в физические пиксели снимка _screenCapture
        int pxX = (int)Math.Round(leftDip * _dpiScaleX);
        int pxY = (int)Math.Round(topDip * _dpiScaleY);
        int pxW = (int)Math.Round(widthDip * _dpiScaleX);
        int pxH = (int)Math.Round(heightDip * _dpiScaleY);

        Close();

        if (pxW > 3 && pxH > 3)
        {
            _onComplete(new System.Drawing.Rectangle(pxX, pxY, pxW, pxH));
        }
        else
        {
            _onComplete(null);
        }
    }

    private void CancelSelection()
    {
        if (_isSelecting)
        {
            _isSelecting = false;
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
