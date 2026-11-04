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

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var win = (Keyboard.Modifiers & ModifierKeys.Windows) != 0;

        // If the key itself is modifier, don't set it as main trigger key yet unless standalone
        if (VirtualKeyHelper.IsModifierKey(vk))
        {
            // Allow remapping standalone CapsLock, Alt, Ctrl, Win if pressed alone
            _viewModel.SetTrigger(vk, false, false, false, false);
            return;
        }

        _viewModel.SetTrigger(vk, ctrl, alt, shift, win);
    }

    private void OnTargetKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var vk = KeyInterop.VirtualKeyFromKey(key);

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var win = (Keyboard.Modifiers & ModifierKeys.Windows) != 0;

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
}
