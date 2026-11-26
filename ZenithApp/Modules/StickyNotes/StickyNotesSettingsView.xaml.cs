using System.Windows.Controls;
using System.Windows.Input;
using ZenithApp.Core.Hotkeys;

namespace ZenithApp.Modules.StickyNotes;

public partial class StickyNotesSettingsView : UserControl
{
    public StickyNotesSettingsView()
    {
        InitializeComponent();
    }

    private void OnCreateHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryExtractHotkey(e, out var modifiers, out var key) &&
            DataContext is StickyNotesSettingsViewModel vm)
        {
            vm.UpdateCreateHotkey(modifiers, key);
        }
    }

    private void OnToggleHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryExtractHotkey(e, out var modifiers, out var key) &&
            DataContext is StickyNotesSettingsViewModel vm)
        {
            vm.UpdateToggleVisibilityHotkey(modifiers, key);
        }
    }

    private static bool TryExtractHotkey(KeyEventArgs e, out KeyModifiers modifiers, out Key key)
    {
        e.Handled = true;
        modifiers = KeyModifiers.None;
        key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl
                or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift
                or Key.LWin or Key.RWin)
        {
            return false;
        }

        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            modifiers |= KeyModifiers.Control;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            modifiers |= KeyModifiers.Shift;
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
            modifiers |= KeyModifiers.Alt;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
            modifiers |= KeyModifiers.Win;

        return modifiers != KeyModifiers.None && key != Key.None;
    }
}
