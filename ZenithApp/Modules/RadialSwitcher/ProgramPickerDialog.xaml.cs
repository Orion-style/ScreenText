using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace ZenithApp.Modules.RadialSwitcher;

public sealed partial class ProgramPickerDialog : FluentWindow
{
    private readonly ZoneEditorItemViewModel _zone;
    private readonly List<ProgramPickerItem> _allPrograms = new();

    public ProgramPickerDialog(ZoneEditorItemViewModel zone)
    {
        InitializeComponent();
        _zone = zone;

        TxtDirectionHeader.Text = $"Сектор: {zone.DirectionName}";
        BtnUnpin.Visibility = zone.IsPinned ? Visibility.Visible : Visibility.Collapsed;

        LoadPrograms();
    }

    private void LoadPrograms()
    {
        _allPrograms.Clear();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Окна верхнего уровня (открытые приложения)
        try
        {
            var openWindows = WindowListService.GetOpenWindows(nint.Zero, maxCount: 50);
            foreach (var win in openWindows)
            {
                if (string.IsNullOrWhiteSpace(win.ProcessName)) continue;
                string key = !string.IsNullOrWhiteSpace(win.ExePath) ? win.ExePath : win.ProcessName;
                if (seenKeys.Add(key))
                {
                    _allPrograms.Add(new ProgramPickerItem
                    {
                        Name = win.ProcessName,
                        ProcessName = win.ProcessName,
                        ExePath = win.ExePath,
                        Icon = win.Icon
                    });
                }
            }
        }
        catch { }

        // 2. Все остальные запущенные процессы с главным окном
        try
        {
            var procs = Process.GetProcesses()
                .Where(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle) && p.Id != Environment.ProcessId)
                .OrderBy(p => p.ProcessName);

            foreach (var p in procs)
            {
                try
                {
                    string procName = p.ProcessName;
                    string exePath = string.Empty;
                    try { exePath = p.MainModule?.FileName ?? string.Empty; } catch { }

                    string key = !string.IsNullOrWhiteSpace(exePath) ? exePath : procName;
                    if (seenKeys.Add(key))
                    {
                        var icon = !string.IsNullOrWhiteSpace(exePath)
                            ? WindowListService.GetIconForPath(exePath)
                            : WindowListService.GetIconForProcessName(procName);

                        _allPrograms.Add(new ProgramPickerItem
                        {
                            Name = procName,
                            ProcessName = procName,
                            ExePath = exePath,
                            Icon = icon
                        });
                    }
                }
                catch { }
            }
        }
        catch { }

        ApplyFilter(string.Empty);
    }

    private void ApplyFilter(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ProgramsList.ItemsSource = _allPrograms;
        }
        else
        {
            var q = query.Trim();
            ProgramsList.ItemsSource = _allPrograms
                .Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            p.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            p.ExePath.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter(TxtSearch.Text);
    }

    private void OnProgramItemSelected(object sender, MouseButtonEventArgs e)
    {
        if (ProgramsList.SelectedItem is ProgramPickerItem selected)
        {
            PinProgram(selected.ProcessName, selected.ExePath, selected.Name);
        }
    }

    private void OnBrowseExeClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Выберите приложение для сектора «{_zone.DirectionName}»",
            Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };

        if (dlg.ShowDialog(this) == true)
        {
            string path = dlg.FileName;
            string procName = Path.GetFileNameWithoutExtension(path);
            PinProgram(procName, path, procName);
        }
    }

    private void PinProgram(string processName, string exePath, string label)
    {
        _zone.PinnedProcessName = processName;
        _zone.PinnedExecutablePath = exePath;
        _zone.CustomLabel = label;
        _zone.IsPinned = true;
        _zone.UpdatePinnedIcon();

        DialogResult = true;
        Close();
    }

    private void OnUnpinClicked(object sender, RoutedEventArgs e)
    {
        _zone.Unpin();
        DialogResult = true;
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
