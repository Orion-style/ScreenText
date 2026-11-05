using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace ZenithApp.Modules.KeyRemap;

public partial class EditRuleDialog : FluentWindow
{
    private readonly EditRuleDialogViewModel _viewModel;
    public KeyRemapRule? ResultRule { get; private set; }

    public EditRuleDialog(KeyRemapRule rule)
    {
        InitializeComponent();
        _viewModel = new EditRuleDialogViewModel(rule);
        DataContext = _viewModel;
    }

    private void OnTriggerKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var vk = KeyInterop.VirtualKeyFromKey(key);

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ||
                   (GetKeyState(VirtualKeyHelper.VK_CONTROL) & 0x8000) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ||
                  (GetKeyState(VirtualKeyHelper.VK_MENU) & 0x8000) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ||
                    (GetKeyState(VirtualKeyHelper.VK_SHIFT) & 0x8000) != 0;
        var win = (Keyboard.Modifiers & ModifierKeys.Windows) != 0 ||
                  (GetKeyState(VirtualKeyHelper.VK_LWIN) & 0x8000) != 0 ||
                  (GetKeyState(VirtualKeyHelper.VK_RWIN) & 0x8000) != 0;

        // If the key itself is modifier, don't set it as main trigger key if other modifiers are pressed
        if (VirtualKeyHelper.IsModifierKey(vk))
        {
            if (VirtualKeyHelper.IsControlKey(vk)) ctrl = true;
            if (VirtualKeyHelper.IsAltKey(vk)) alt = true;
            if (VirtualKeyHelper.IsShiftKey(vk)) shift = true;
            if (VirtualKeyHelper.IsWinKey(vk)) win = true;

            int activeModCount = (ctrl ? 1 : 0) + (alt ? 1 : 0) + (shift ? 1 : 0) + (win ? 1 : 0);
            if (activeModCount <= 1)
            {
                // Standalone modifier key remap
                _viewModel.SetTrigger(vk, false, false, false, false);
            }
            else
            {
                _viewModel.SetTriggerModifiers(ctrl, alt, shift, win);
            }
            return;
        }

        _viewModel.SetTrigger(vk, ctrl, alt, shift, win);
    }

    private void OnTargetKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var vk = KeyInterop.VirtualKeyFromKey(key);

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ||
                   (GetKeyState(VirtualKeyHelper.VK_CONTROL) & 0x8000) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ||
                  (GetKeyState(VirtualKeyHelper.VK_MENU) & 0x8000) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ||
                    (GetKeyState(VirtualKeyHelper.VK_SHIFT) & 0x8000) != 0;
        var win = (Keyboard.Modifiers & ModifierKeys.Windows) != 0 ||
                  (GetKeyState(VirtualKeyHelper.VK_LWIN) & 0x8000) != 0 ||
                  (GetKeyState(VirtualKeyHelper.VK_RWIN) & 0x8000) != 0;

        if (VirtualKeyHelper.IsModifierKey(vk))
        {
            if (_viewModel.ActionTypeIndex == 1)
            {
                if (VirtualKeyHelper.IsControlKey(vk)) ctrl = true;
                if (VirtualKeyHelper.IsAltKey(vk)) alt = true;
                if (VirtualKeyHelper.IsShiftKey(vk)) shift = true;
                if (VirtualKeyHelper.IsWinKey(vk)) win = true;

                int activeModCount = (ctrl ? 1 : 0) + (alt ? 1 : 0) + (shift ? 1 : 0) + (win ? 1 : 0);
                if (activeModCount <= 1)
                {
                    _viewModel.SetTarget(vk, false, false, false, false);
                }
                else
                {
                    _viewModel.SetTargetModifiers(ctrl, alt, shift, win);
                }
            }
            else
            {
                _viewModel.SetTarget(vk, false, false, false, false);
            }
            return;
        }

        _viewModel.SetTarget(vk, ctrl, alt, shift, win);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Validate())
        {
            ResultRule = _viewModel.BuildResult();
            DialogResult = true;
            Close();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
