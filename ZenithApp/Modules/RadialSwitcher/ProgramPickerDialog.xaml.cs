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

    private static readonly HashSet<string> _ignoreWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "uninstall", "удалить", "деинсталлировать", "deinstall", "help", "readme",
        "documentation", "справка", "website", "конфигурация", "settings", "installer"
    };

    public ProgramPickerDialog(ZoneEditorItemViewModel zone)
    {
        InitializeComponent();
        _zone = zone;

        BtnUnpin.Visibility = zone.IsPinned ? Visibility.Visible : Visibility.Collapsed;

        LoadPrograms();
    }

    private void LoadPrograms()
    {
        _allPrograms.Clear();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Установленные программы из меню «Пуск» (общие и пользователя)
        var startMenuPaths = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
        };

        foreach (var dir in startMenuPaths)
        {
            if (!Directory.Exists(dir)) continue;

            try
            {
                var lnkFiles = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories);
                foreach (var lnk in lnkFiles)
                {
                    try
                    {
                        string name = Path.GetFileNameWithoutExtension(lnk).Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        // Игнорируем деинсталляторы и справочные файлы
                        if (_ignoreWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        if (seenNames.Add(name))
                        {
                            var icon = WindowListService.GetIconForPath(lnk);
                            _allPrograms.Add(new ProgramPickerItem
                            {
                                Name = name,
                                ProcessName = name,
                                ExePath = lnk,
                                Icon = icon
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // 2. Открытые окна верхнего уровня
        try
        {
            var openWindows = WindowListService.GetOpenWindows(nint.Zero, maxCount: 60);
            foreach (var win in openWindows)
            {
                if (string.IsNullOrWhiteSpace(win.ProcessName)) continue;

                if (seenNames.Add(win.ProcessName))
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

        // 3. Другие запущенные процессы с главным окном
        try
        {
            var procs = Process.GetProcesses()
                .Where(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle) && p.Id != Environment.ProcessId);

            foreach (var p in procs)
            {
                try
                {
                    string procName = p.ProcessName;
                    if (seenNames.Add(procName))
                    {
                        string exePath = string.Empty;
                        try { exePath = p.MainModule?.FileName ?? string.Empty; } catch { }

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

        // Сортировка по алфавиту
        _allPrograms.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

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
                            p.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase))
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
            Title = "Выберите приложение",
            Filter = "Исполняемые файлы (*.exe;*.lnk)|*.exe;*.lnk|Все файлы (*.*)|*.*",
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
