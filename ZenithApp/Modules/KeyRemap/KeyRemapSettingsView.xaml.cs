using System.Windows;
using System.Windows.Controls;

namespace ZenithApp.Modules.KeyRemap;

public partial class KeyRemapSettingsView : UserControl
{
    public KeyRemapSettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is KeyRemapSettingsViewModel vm)
        {
            vm.OpenEditDialogRequested -= OnOpenEditDialogRequested;
            vm.OpenEditDialogRequested += OnOpenEditDialogRequested;
        }
    }

    private void OnAddRuleClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is KeyRemapSettingsViewModel vm)
        {
            var newRule = new KeyRemapRule();
            var dlg = new EditRuleDialog(newRule)
            {
                Owner = Window.GetWindow(this)
            };

            if (dlg.ShowDialog() == true && dlg.ResultRule != null)
            {
                vm.AddRule(dlg.ResultRule);
            }
        }
    }

    private void OnOpenEditDialogRequested(KeyRemapRule rule, Action<KeyRemapRule> onSave)
    {
        var dlg = new EditRuleDialog(rule)
        {
            Owner = Window.GetWindow(this)
        };

        if (dlg.ShowDialog() == true && dlg.ResultRule != null)
        {
            onSave(dlg.ResultRule);
        }
    }
}
