using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ZenithApp.Core.Hotkeys;

namespace ZenithApp.Modules.NextSession;

public partial class NextSessionSettingsView : UserControl
{
    private readonly DispatcherTimer _refreshTimer;

    public NextSessionSettingsView()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _refreshTimer.Tick += OnRefreshTimerTick;

        IsVisibleChanged += OnIsVisibleChanged;
        Unloaded += OnUnloaded;
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        if (IsVisible && DataContext is NextSessionSettingsViewModel vm)
        {
            await vm.RefreshRunningAppsAsync();
        }
    }

    private async void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            if (DataContext is NextSessionSettingsViewModel vm)
            {
                await vm.RefreshRunningAppsAsync();
            }
            _refreshTimer.Start();
        }
        else
        {
            _refreshTimer.Stop();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
    }

    private void OnHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl
                or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift
                or Key.LWin or Key.RWin)
        {
            return;
        }

        var modifiers = KeyModifiers.None;
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            modifiers |= KeyModifiers.Control;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            modifiers |= KeyModifiers.Shift;
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
            modifiers |= KeyModifiers.Alt;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
            modifiers |= KeyModifiers.Win;

        if (DataContext is NextSessionSettingsViewModel vm)
        {
            vm.UpdateHotkey(modifiers, key);
        }
    }
}
