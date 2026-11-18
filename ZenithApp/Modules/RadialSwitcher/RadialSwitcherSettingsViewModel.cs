using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using ZenithApp.Core.Settings;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.RadialSwitcher;

public sealed class RadialSwitcherSettingsViewModel : ViewModelBase
{
    private readonly RadialSwitcherModule _module;
    private readonly ISettingsService _settingsService;
    private ZoneEditorItemViewModel? _selectedZone;

    public RadialSwitcherSettings Settings => _settingsService.Current.RadialSwitcher;

    public ObservableCollection<ZoneEditorItemViewModel> Zones { get; } = new();
    public ObservableCollection<string> RunningProcessNames { get; } = new();

    public event Action? OnWheelRedrawRequested;

    public ZoneEditorItemViewModel? SelectedZone
    {
        get => _selectedZone;
        set
        {
            if (_selectedZone != value)
            {
                _selectedZone = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedZone));
                OnWheelRedrawRequested?.Invoke();
            }
        }
    }

    public bool HasSelectedZone => SelectedZone != null;

    public int HoldDelayMs
    {
        get => Settings.HoldDelayMs;
        set
        {
            if (Settings.HoldDelayMs != value)
            {
                Settings.HoldDelayMs = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HoldDelayText));
                _settingsService.Save();
            }
        }
    }

    public string HoldDelayText => $"{HoldDelayMs} мс";

    public int SelectedZoneCountIndex
    {
        get => Settings.ZoneCount switch
        {
            4 => 0,
            6 => 1,
            _ => 2 // 8 зон
        };
        set
        {
            int newCount = value switch
            {
                0 => 4,
                1 => 6,
                _ => 8
            };

            if (Settings.ZoneCount != newCount)
            {
                Settings.ZoneCount = newCount;
                Settings.EnsureZonesConfigured();
                OnPropertyChanged();
                _settingsService.Save();
                RefreshZonesList();
            }
        }
    }

    public bool AutoLaunchPinned
    {
        get => Settings.AutoLaunchPinned;
        set
        {
            if (Settings.AutoLaunchPinned != value)
            {
                Settings.AutoLaunchPinned = value;
                OnPropertyChanged();
                _settingsService.Save();
            }
        }
    }

    public ICommand ResetZonesCommand { get; }
    public ICommand RefreshProcessesCommand { get; }

    public RadialSwitcherSettingsViewModel(RadialSwitcherModule module, ISettingsService settingsService)
    {
        _module = module;
        _settingsService = settingsService;

        Settings.EnsureZonesConfigured();

        ResetZonesCommand = new RelayCommand(_ => ResetAllZones());
        RefreshProcessesCommand = new RelayCommand(_ => RefreshRunningProcesses());

        RefreshRunningProcesses();
        RefreshZonesList();
    }

    public void RefreshRunningProcesses()
    {
        RunningProcessNames.Clear();
        RunningProcessNames.Add("(Выберите процесс)");

        try
        {
            var procs = Process.GetProcesses()
                .Where(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle) && p.Id != Environment.ProcessId)
                .Select(p => p.ProcessName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name);

            foreach (var procName in procs)
            {
                RunningProcessNames.Add(procName);
            }
        }
        catch { }
    }

    public void RefreshZonesList()
    {
        int prevSelectedIdx = SelectedZone?.Index ?? 0;
        Zones.Clear();
        Settings.EnsureZonesConfigured();

        foreach (var zone in Settings.CustomZones)
        {
            var item = new ZoneEditorItemViewModel(zone, this, () => _settingsService.Save());
            item.UpdatePinnedIcon();
            Zones.Add(item);
        }

        SelectedZone = Zones.FirstOrDefault(z => z.Index == prevSelectedIdx) ?? Zones.FirstOrDefault();
        OnWheelRedrawRequested?.Invoke();
    }

    public void NotifyZoneChanged(ZoneEditorItemViewModel item)
    {
        OnPropertyChanged(nameof(SelectedZone));
        OnWheelRedrawRequested?.Invoke();
    }

    private void ResetAllZones()
    {
        Settings.CustomZones.Clear();
        Settings.EnsureZonesConfigured();
        _settingsService.Save();
        RefreshZonesList();
    }
}

public sealed class ZoneEditorItemViewModel : ViewModelBase
{
    private readonly RadialZoneConfig _config;
    private readonly RadialSwitcherSettingsViewModel _parent;
    private readonly Action _onSave;

    private ImageSource? _pinnedIcon;
    private string? _selectedRunningProcess = "(Выберите процесс)";

    public int Index => _config.Index;
    public string DirectionName => _config.DirectionName;

    public bool IsPinned
    {
        get => _config.IsPinned;
        set
        {
            if (_config.IsPinned != value)
            {
                _config.IsPinned = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusDescription));
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(IsUnpinned));
                _onSave();
                _parent.NotifyZoneChanged(this);
            }
        }
    }

    public bool IsUnpinned => !IsPinned;

    public string PinnedProcessName
    {
        get => _config.PinnedProcessName ?? string.Empty;
        set
        {
            var val = string.IsNullOrWhiteSpace(value) || value == "(Выберите процесс)" ? null : value.Trim();
            if (_config.PinnedProcessName != val)
            {
                _config.PinnedProcessName = val;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusDescription));
                OnPropertyChanged(nameof(StatusBadgeText));
                UpdatePinnedIcon();
                _onSave();
                _parent.NotifyZoneChanged(this);
            }
        }
    }

    public string PinnedExecutablePath
    {
        get => _config.PinnedExecutablePath ?? string.Empty;
        set
        {
            var val = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_config.PinnedExecutablePath != val)
            {
                _config.PinnedExecutablePath = val;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusDescription));
                UpdatePinnedIcon();
                _onSave();
                _parent.NotifyZoneChanged(this);
            }
        }
    }

    public string CustomLabel
    {
        get => _config.CustomLabel ?? string.Empty;
        set
        {
            var val = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_config.CustomLabel != val)
            {
                _config.CustomLabel = val;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusDescription));
                _onSave();
                _parent.NotifyZoneChanged(this);
            }
        }
    }

    public ImageSource? PinnedIcon
    {
        get => _pinnedIcon;
        private set
        {
            if (_pinnedIcon != value)
            {
                _pinnedIcon = value;
                OnPropertyChanged();
            }
        }
    }

    public string StatusBadgeText => IsPinned && !string.IsNullOrWhiteSpace(PinnedProcessName)
        ? "Закреплено"
        : "Свободный сектор";

    public string StatusDescription
    {
        get
        {
            if (IsPinned && !string.IsNullOrWhiteSpace(PinnedProcessName))
            {
                return $"Закреплено: {PinnedProcessName}" + (!string.IsNullOrWhiteSpace(CustomLabel) ? $" ({CustomLabel})" : "");
            }
            return "Автоматически (Свободный сектор: недавние окна по MRU-приоритету)";
        }
    }

    public string? SelectedRunningProcess
    {
        get => _selectedRunningProcess;
        set
        {
            if (_selectedRunningProcess != value)
            {
                _selectedRunningProcess = value;
                OnPropertyChanged();
                if (!string.IsNullOrWhiteSpace(value) && value != "(Выберите процесс)")
                {
                    SetFromRunningProcess(value);
                }
            }
        }
    }

    public ObservableCollection<string> RunningProcessNames => _parent.RunningProcessNames;

    public ICommand BrowseExeCommand { get; }
    public ICommand UnpinCommand { get; }
    public ICommand ResetZoneCommand { get; }

    public ZoneEditorItemViewModel(RadialZoneConfig config, RadialSwitcherSettingsViewModel parent, Action onSave)
    {
        _config = config;
        _parent = parent;
        _onSave = onSave;

        BrowseExeCommand = new RelayCommand(_ => ChooseExeFile());
        UnpinCommand = new RelayCommand(_ => Unpin());
        ResetZoneCommand = new RelayCommand(_ => Unpin());

        if (IsPinned && !string.IsNullOrWhiteSpace(PinnedProcessName))
        {
            _selectedRunningProcess = PinnedProcessName;
        }
    }

    public void UpdatePinnedIcon()
    {
        if (!string.IsNullOrWhiteSpace(_config.PinnedExecutablePath))
        {
            PinnedIcon = WindowListService.GetIconForPath(_config.PinnedExecutablePath);
        }
        else if (!string.IsNullOrWhiteSpace(_config.PinnedProcessName))
        {
            PinnedIcon = WindowListService.GetIconForProcessName(_config.PinnedProcessName);
        }
        else
        {
            PinnedIcon = null;
        }
    }

    private void ChooseExeFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Выберите приложение для сектора «{DirectionName}»",
            Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };

        if (dlg.ShowDialog() == true)
        {
            string path = dlg.FileName;
            string procName = Path.GetFileNameWithoutExtension(path);

            _config.PinnedExecutablePath = path;
            _config.PinnedProcessName = procName;
            _config.IsPinned = true;

            if (string.IsNullOrWhiteSpace(_config.CustomLabel))
            {
                _config.CustomLabel = procName;
            }

            _selectedRunningProcess = procName;

            OnPropertyChanged(nameof(IsPinned));
            OnPropertyChanged(nameof(IsUnpinned));
            OnPropertyChanged(nameof(PinnedProcessName));
            OnPropertyChanged(nameof(PinnedExecutablePath));
            OnPropertyChanged(nameof(CustomLabel));
            OnPropertyChanged(nameof(StatusDescription));
            OnPropertyChanged(nameof(StatusBadgeText));
            OnPropertyChanged(nameof(SelectedRunningProcess));

            UpdatePinnedIcon();
            _onSave();
            _parent.NotifyZoneChanged(this);
        }
    }

    private void SetFromRunningProcess(string procName)
    {
        _config.PinnedProcessName = procName;
        _config.IsPinned = true;

        if (string.IsNullOrWhiteSpace(_config.CustomLabel))
        {
            _config.CustomLabel = procName;
        }

        try
        {
            var procs = Process.GetProcessesByName(procName);
            foreach (var p in procs)
            {
                try
                {
                    string path = p.MainModule?.FileName ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        _config.PinnedExecutablePath = path;
                        break;
                    }
                }
                catch { }
            }
        }
        catch { }

        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(IsUnpinned));
        OnPropertyChanged(nameof(PinnedProcessName));
        OnPropertyChanged(nameof(PinnedExecutablePath));
        OnPropertyChanged(nameof(CustomLabel));
        OnPropertyChanged(nameof(StatusDescription));
        OnPropertyChanged(nameof(StatusBadgeText));

        UpdatePinnedIcon();
        _onSave();
        _parent.NotifyZoneChanged(this);
    }

    public void Unpin()
    {
        _config.IsPinned = false;
        _config.PinnedProcessName = null;
        _config.PinnedExecutablePath = null;
        _config.CustomLabel = null;
        PinnedIcon = null;
        _selectedRunningProcess = "(Выберите процесс)";

        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(IsUnpinned));
        OnPropertyChanged(nameof(PinnedProcessName));
        OnPropertyChanged(nameof(PinnedExecutablePath));
        OnPropertyChanged(nameof(CustomLabel));
        OnPropertyChanged(nameof(StatusDescription));
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(SelectedRunningProcess));

        _onSave();
        _parent.NotifyZoneChanged(this);
    }
}
