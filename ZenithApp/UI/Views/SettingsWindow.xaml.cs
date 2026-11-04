using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using ZenithApp.UI.ViewModels;

namespace ZenithApp.UI.Views;

public partial class SettingsWindow : FluentWindow
{
    private readonly SettingsViewModel _viewModel;
    private ModuleItemViewModel? _activeModule;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        ShowGeneralPage();
    }

    private void OnGeneralNavClicked(object sender, MouseButtonEventArgs e)
    {
        ModulesList.SelectedItem = null;
        ShowGeneralPage();
    }

    private void ShowGeneralPage()
    {
        GeneralPage.Visibility = Visibility.Visible;
        ModuleDetailPage.Visibility = Visibility.Collapsed;

        BtnNavGeneral.Background = (Brush)FindResource("SubtleFillColorTertiaryBrush");
    }

    private void OnModuleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModulesList.SelectedItem is ModuleItemViewModel moduleVm)
        {
            _activeModule = moduleVm;
            BtnNavGeneral.Background = Brushes.Transparent;
            GeneralPage.Visibility = Visibility.Collapsed;
            ModuleDetailPage.Visibility = Visibility.Visible;

            TxtModuleName.Text = moduleVm.Name;
            TxtModuleDesc.Text = moduleVm.Description;
            UpdateModuleSwitchState();

            if (moduleVm.Id == ZenithApp.Modules.Screenshot.ScreenshotModule.ModuleId)
            {
                ScreenshotConfigContainer.Visibility = Visibility.Visible;
                SmartLinksConfigContainer.Visibility = Visibility.Collapsed;
                KeyRemapConfigContainer.Visibility = Visibility.Collapsed;
                CardConfigPlaceholder.Visibility = Visibility.Collapsed;
            }
            else if (moduleVm.Id == ZenithApp.Modules.SmartLinks.SmartLinksModule.ModuleId)
            {
                _viewModel.SmartLinksSettings?.RefreshState();
                ScreenshotConfigContainer.Visibility = Visibility.Collapsed;
                SmartLinksConfigContainer.Visibility = Visibility.Visible;
                KeyRemapConfigContainer.Visibility = Visibility.Collapsed;
                CardConfigPlaceholder.Visibility = Visibility.Collapsed;
            }
            else if (moduleVm.Id == ZenithApp.Modules.KeyRemap.KeyRemapModule.ModuleId)
            {
                _viewModel.KeyRemapSettings?.ValidateConflicts();
                ScreenshotConfigContainer.Visibility = Visibility.Collapsed;
                SmartLinksConfigContainer.Visibility = Visibility.Collapsed;
                KeyRemapConfigContainer.Visibility = Visibility.Visible;
                CardConfigPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                ScreenshotConfigContainer.Visibility = Visibility.Collapsed;
                SmartLinksConfigContainer.Visibility = Visibility.Collapsed;
                KeyRemapConfigContainer.Visibility = Visibility.Collapsed;
                CardConfigPlaceholder.Visibility = Visibility.Visible;
            }
        }
    }

    private void OnModuleToggleClicked(object sender, RoutedEventArgs e)
    {
        if (_activeModule != null)
        {
            _activeModule.IsEnabled = SwitchModuleState.IsChecked == true;
            UpdateModuleStatusText(_activeModule.IsEnabled);
        }
    }

    private void UpdateModuleSwitchState()
    {
        if (_activeModule != null)
        {
            SwitchModuleState.IsChecked = _activeModule.IsEnabled;
            UpdateModuleStatusText(_activeModule.IsEnabled);
        }
    }

    private void UpdateModuleStatusText(bool isEnabled)
    {
        TxtModuleStatus.Text = isEnabled 
            ? (string)FindResource("Module_StatusActive") 
            : (string)FindResource("Module_StatusInactive");
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        DataContext = null;
    }
}
