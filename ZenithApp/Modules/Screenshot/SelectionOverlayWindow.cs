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

    private System.Windows.Point _startPointDips;
    private bool _isSelecting;
    private readonly Canvas _canvas;
    private readonly System.Windows.Shapes.Rectangle _selectionRect;
    private readonly System.Windows.Controls.Image _bgImage;
    private readonly System.Windows.Shapes.Rectangle _dimOverlay;

    // Масштаб DPI между экранными физическими пикселями и WPF DIPs
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    public SelectionOverlayWindow(System.Drawing.Rectangle virtualBounds, Bitmap screenCapture, Action<System.Drawing.Rectangle?> onComplete)
    {
        _virtualBounds = virtualBounds;
        _screenCapture = screenCapture;
        _onComplete = onComplete;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Background = System.Windows.Media.Brushes.Black;

        _canvas = new Canvas { Background = System.Windows.Media.Brushes.Transparent };
        Content = _canvas;

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

        // Рамка выделения
        _selectionRect = new System.Windows.Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 0, 120, 215)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 0, 120, 215)),
            Visibility = Visibility.Collapsed
        };
        _canvas.Children.Add(_selectionRect);

        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
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

        Focus();
        CaptureMouse();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelSelection();
        }
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
        if (!_isSelecting) return;

        var currentPoint = e.GetPosition(_canvas);
        var x = Math.Min(_startPointDips.X, currentPoint.X);
        var y = Math.Min(_startPointDips.Y, currentPoint.Y);
        var w = Math.Abs(currentPoint.X - _startPointDips.X);
        var h = Math.Abs(currentPoint.Y - _startPointDips.Y);

        Canvas.SetLeft(_selectionRect, x);
        Canvas.SetTop(_selectionRect, y);
        _selectionRect.Width = w;
        _selectionRect.Height = h;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting) return;
        _isSelecting = false;
        ReleaseMouseCapture();

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
