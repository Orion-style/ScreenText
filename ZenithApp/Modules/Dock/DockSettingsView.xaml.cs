using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ZenithApp.Modules.Dock;

public partial class DockSettingsView : UserControl
{
    public DockSettingsView()
    {
        InitializeComponent();
    }

    private void OnAddFileClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is DockSettingsViewModel vm)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Выберите исполняемый файл или ярлык",
                Filter = "Приложения и ярлыки (*.exe;*.lnk)|*.exe;*.lnk|Все файлы (*.*)|*.*",
                Multiselect = true
            };

            if (ofd.ShowDialog() == true)
            {
                foreach (var fileName in ofd.FileNames)
                {
                    vm.AddItem(fileName);
                }
            }
        }
    }
}
