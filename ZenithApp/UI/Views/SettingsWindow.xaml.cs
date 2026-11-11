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

            HideAllConfigContainers();

            if (moduleVm.Id == ZenithApp.Modules.Screenshot.ScreenshotModule.ModuleId)
            {
                ScreenshotConfigContainer.Visibility = Visibility.Visible;
            }
            else if (moduleVm.Id == ZenithApp.Modules.SmartLinks.SmartLinksModule.ModuleId)
            {
                _viewModel.SmartLinksSettings?.RefreshState();
                SmartLinksConfigContainer.Visibility = Visibility.Visible;
            }
            else if (moduleVm.Id == ZenithApp.Modules.KeyRemap.KeyRemapModule.ModuleId)
            {
                _viewModel.KeyRemapSettings?.ValidateConflicts();
                KeyRemapConfigContainer.Visibility = Visibility.Visible;
            }
            else if (moduleVm.Id == ZenithApp.Modules.QuickLook.QuickLookModule.ModuleId)
            {
                QuickLookConfigContainer.Visibility = Visibility.Visible;
            }
            else if (moduleVm.Id == ZenithApp.Modules.RadialSwitcher.RadialSwitcherModule.ModuleId)
            {
                RadialSwitcherConfigContainer.Visibility = Visibility.Visible;
            }
            else if (moduleVm.Id == ZenithApp.Modules.SnapZones.SnapZonesModule.ModuleId)
            {
                SnapZonesConfigContainer.Visibility = Visibility.Visible;
            }
            else if (moduleVm.Id == ZenithApp.Modules.StartupManager.StartupManagerModule.ModuleId)
            {
                _viewModel.StartupManagerSettings?.Refresh();
                StartupManagerConfigContainer.Visibility = Visibility.Visible;
            }
            else
            {
                CardConfigPlaceholder.Visibility = Visibility.Visible;
            }
        }
    }

    private void HideAllConfigContainers()
    {
        ScreenshotConfigContainer.Visibility = Visibility.Collapsed;
        SmartLinksConfigContainer.Visibility = Visibility.Collapsed;
        KeyRemapConfigContainer.Visibility = Visibility.Collapsed;
        QuickLookConfigContainer.Visibility = Visibility.Collapsed;
        SnapZonesConfigContainer.Visibility = Visibility.Collapsed;
        RadialSwitcherConfigContainer.Visibility = Visibility.Collapsed;
        StartupManagerConfigContainer.Visibility = Visibility.Collapsed;
        CardConfigPlaceholder.Visibility = Visibility.Collapsed;
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
