using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ZenithApp.Modules.Dock;

public sealed class DockTriggerWindow : Window
{
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int GWL_EXSTYLE = -20;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    private readonly DockModule _module;

    public DockTriggerWindow(DockModule module)
    {
        _module = module;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Height = 4; // ultra-thin 4px trigger zone at bottom edge

        Loaded += OnLoaded;
        MouseEnter += OnMouseEnter;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        var exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
        SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

        UpdatePosition();
    }

    public void UpdatePosition()
    {
        Dispatcher.Invoke(() =>
        {
            var bounds = ScreenHelper.GetTargetScreenBounds(_module.Settings.MonitorTarget, this);

            Left = bounds.Left;
            Width = bounds.Width;
            Top = bounds.Top + bounds.Height - 4;
        });
    }

    private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // Don't show dock if full-screen game / video is playing
        if (ProcessInteractionService.IsForegroundWindowFullScreen())
        {
            return;
        }

        _module.OnEdgeTriggerActivated();
    }
}
