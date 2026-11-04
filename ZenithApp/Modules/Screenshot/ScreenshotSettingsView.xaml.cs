using System.Windows.Controls;
using System.Windows.Input;
using ZenithApp.Core.Hotkeys;

namespace ZenithApp.Modules.Screenshot;

public partial class ScreenshotSettingsView : UserControl
{
    public ScreenshotSettingsView()
    {
        InitializeComponent();
    }

    private void OnScreenshotHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (IsModifierOnly(key)) return;

        var modifiers = GetCurrentModifiers();
        if (DataContext is ScreenshotSettingsViewModel vm)
        {
            vm.UpdateScreenshotHotkey(modifiers, key);
        }
    }

    private void OnOcrHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (IsModifierOnly(key)) return;

        var modifiers = GetCurrentModifiers();
        if (DataContext is ScreenshotSettingsViewModel vm)
        {
            vm.UpdateOcrHotkey(modifiers, key);
        }
    }

    private static bool IsModifierOnly(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
                   or Key.LeftAlt or Key.RightAlt
                   or Key.LeftShift or Key.RightShift
                   or Key.LWin or Key.RWin;
    }

    private static KeyModifiers GetCurrentModifiers()
    {
        var modifiers = KeyModifiers.None;
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            modifiers |= KeyModifiers.Control;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            modifiers |= KeyModifiers.Shift;
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
            modifiers |= KeyModifiers.Alt;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
            modifiers |= KeyModifiers.Win;

        return modifiers;
    }
}
