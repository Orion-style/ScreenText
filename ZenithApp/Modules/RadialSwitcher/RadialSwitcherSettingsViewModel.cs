using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using ZenithApp.Core.Settings;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.RadialSwitcher;

public sealed class RadialSwitcherSettingsViewModel : ViewModelBase
{
    private readonly RadialSwitcherModule _module;
    private readonly ISettingsService _settingsService;

    public RadialSwitcherSettings Settings => _settingsService.Current.RadialSwitcher;

    public ObservableCollection<ZoneEditorItemViewModel> Zones { get; } = new();
    public ObservableCollection<string> RunningProcessNames { get; } = new();

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
        Zones.Clear();
        Settings.EnsureZonesConfigured();

        foreach (var zone in Settings.CustomZones)
        {
            var item = new ZoneEditorItemViewModel(zone, this, () => _settingsService.Save());
            Zones.Add(item);
        }
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
                _onSave();
            }
        }
    }

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
                _onSave();
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
            }
        }
    }

    public string StatusDescription
    {
        get
        {
            if (IsPinned && !string.IsNullOrWhiteSpace(PinnedProcessName))
            {
                return $"Закреплено: {PinnedProcessName}" + (!string.IsNullOrWhiteSpace(CustomLabel) ? $" ({CustomLabel})" : "");
            }
            return "Автоматически (Недавнее активное окно)";
        }
    }

    public ObservableCollection<string> RunningProcessNames => _parent.RunningProcessNames;

    public ICommand ResetZoneCommand { get; }

    public ZoneEditorItemViewModel(RadialZoneConfig config, RadialSwitcherSettingsViewModel parent, Action onSave)
    {
        _config = config;
        _parent = parent;
        _onSave = onSave;

        ResetZoneCommand = new RelayCommand(_ =>
        {
            IsPinned = false;
            PinnedProcessName = string.Empty;
            CustomLabel = string.Empty;
        });
    }
}
