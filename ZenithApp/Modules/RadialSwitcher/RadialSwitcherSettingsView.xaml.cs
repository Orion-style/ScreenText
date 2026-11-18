using System.Windows;
using System.Windows.Controls;

namespace ZenithApp.Modules.RadialSwitcher;

public partial class RadialSwitcherSettingsView : UserControl
{
    public RadialSwitcherSettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is RadialSwitcherSettingsViewModel oldVm)
        {
            oldVm.OpenProgramPickerRequested -= OnOpenProgramPickerRequested;
        }

        if (e.NewValue is RadialSwitcherSettingsViewModel newVm)
        {
            newVm.OpenProgramPickerRequested -= OnOpenProgramPickerRequested;
            newVm.OpenProgramPickerRequested += OnOpenProgramPickerRequested;
        }
    }

    private void OnOpenProgramPickerRequested(ZoneEditorItemViewModel zone)
    {
        var parentWindow = Window.GetWindow(this);
        var dlg = new ProgramPickerDialog(zone)
        {
            Owner = parentWindow
        };
        dlg.ShowDialog();
    }
}
