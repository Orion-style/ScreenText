using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ZenithApp.Modules.T9Predictor;

public partial class T9InlineHintOverlay : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    public T9InlineHintOverlay()
    {
        InitializeComponent();

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        nint exStyle = nint.Size == 8 ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE) : GetWindowLong32(hwnd, GWL_EXSTYLE);
        exStyle = (nint)((long)exStyle | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        if (nint.Size == 8)
            SetWindowLongPtr64(hwnd, GWL_EXSTYLE, exStyle);
        else
            SetWindowLong32(hwnd, GWL_EXSTYLE, (int)exStyle);
    }

    public void ShowHint(string suffixText, Point screenPos, double caretHeight, bool isDarkBg, double opacity)
    {
        TxtSuggestion.Text = suffixText;

        // Adaptive contrast coloring
        if (isDarkBg)
        {
            // Light grey text on dark background
            TxtSuggestion.Foreground = new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(opacity, 0.2, 0.9)), 220, 220, 220));
        }
        else
        {
            // Dark grey text on light background
            TxtSuggestion.Foreground = new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(opacity, 0.2, 0.9)), 80, 80, 80));
        }

        // Approximate font size by caret height
        double fontSize = Math.Clamp(caretHeight * 0.85, 11, 24);
        TxtSuggestion.FontSize = fontSize;

        TxtSuggestion.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double dipW = TxtSuggestion.DesiredSize.Width + 4;
        double dipH = Math.Max(caretHeight, TxtSuggestion.DesiredSize.Height);

        Width = dipW;
        Height = dipH;

        Left = (screenPos.X / _dpiScaleX) + 1;
        Top = (screenPos.Y / _dpiScaleY);

        if (Visibility != Visibility.Visible)
        {
            Show();
        }
    }

    public void HideHint()
    {
        if (Visibility == Visibility.Visible)
        {
            Hide();
        }
    }
}
