using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace ZenithApp.Modules.NextSession;

public partial class NextSessionWindow : FluentWindow
{
    private readonly NextSessionWindowViewModel _viewModel;

    public NextSessionWindow(NextSessionWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += async (_, _) =>
        {
            await _viewModel.LoadProgramsAsync();
            TxtMessage.Focus();
        };
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnProgramsScrollPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ProgramsScrollViewer.ScrollableHeight > 0)
        {
            double delta = (e.Delta / 120.0) * 36.0;
            double target = Math.Clamp(
                ProgramsScrollViewer.VerticalOffset - delta,
                0,
                ProgramsScrollViewer.ScrollableHeight);
            ProgramsScrollViewer.ScrollToVerticalOffset(target);
            e.Handled = true;
        }
    }

    private void OnAddCustomProgramClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите программу или файл для запуска",
            Filter = "Программы и ярлыки (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };

        if (dlg.ShowDialog(this) == true)
        {
            _viewModel.AddCustomProgram(dlg.FileName);
        }
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.SavePlan(showToast: true);
        Close();
    }

    private void OnSaveAndRestartClicked(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            "Сохранить план следующего сеанса и перезагрузить компьютер сейчас?",
            "Подтверждение перезагрузки",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            _viewModel.SaveAndRestart();
            Close();
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
