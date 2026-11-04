using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Settings;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.SnapZones;

public sealed class PresetOptionItem
{
    public ZoneLayoutPreset Preset { get; }
    public string Name { get; }
    public string Description { get; }

    public PresetOptionItem(ZoneLayoutPreset preset, string name, string description)
    {
        Preset = preset;
        Name = name;
        Description = description;
    }
}

public sealed class MonitorPresetItem : ViewModelBase
{
    private string _selectedPresetName;

    public string DeviceName { get; }
    public string DisplayName { get; }

    public string SelectedPresetName
    {
        get => _selectedPresetName;
        set => SetProperty(ref _selectedPresetName, value);
    }

    public MonitorPresetItem(string deviceName, string displayName, string selectedPresetName)
    {
        DeviceName = deviceName;
        DisplayName = displayName;
        _selectedPresetName = selectedPresetName;
    }
}

public sealed class SnapZonesSettingsViewModel : ViewModelBase
{
    private readonly SnapZonesModule _module;
    private readonly ISettingsService _settingsService;
    private string _newProcessName = string.Empty;

    public SnapZonesSettings Settings => _settingsService.Current.SnapZones;

    public List<PresetOptionItem> PresetOptions { get; } = new()
    {
        new(ZoneLayoutPreset.TwoColumns5050, "2 колонки (50/50)", "Экран делится пополам по вертикали"),
        new(ZoneLayoutPreset.ThreeColumns, "3 колонки", "Три равные вертикальные зоны"),
        new(ZoneLayoutPreset.TwoColumns6633, "2 колонки (66 / 33)", "Широкая главная зона слева и узкая справа"),
        new(ZoneLayoutPreset.TwoColumns3366, "2 колонки (33 / 66)", "Узкая зона слева и широкая главная справа"),
        new(ZoneLayoutPreset.TwoRows, "2 ряда", "Экран делится пополам по горизонтали"),
        new(ZoneLayoutPreset.Grid2x2, "Сетка 2x2", "Четыре равных квадранта"),
        new(ZoneLayoutPreset.PriorityGrid, "Главная + 2 боковых", "50% зона слева и две равные зоны 25% справа"),
        new(ZoneLayoutPreset.CenterSingle, "По центру", "Одна просторная центральная область"),
        new(ZoneLayoutPreset.Custom, "Пользовательская", "Настраиваемая сетка колонок и рядов")
    };

    public ObservableCollection<MonitorPresetItem> MonitorPresets { get; } = new();
    public ObservableCollection<string> ExcludedProcesses { get; } = new();

    public ICommand AddExcludedProcessCommand { get; }
    public ICommand RemoveExcludedProcessCommand { get; }

    public SnapZonesSettingsViewModel(SnapZonesModule module, ISettingsService settingsService)
    {
        _module = module;
        _settingsService = settingsService;

        foreach (var p in Settings.ExcludedProcesses)
        {
            ExcludedProcesses.Add(p);
        }

        RefreshMonitors();

        AddExcludedProcessCommand = new RelayCommand(AddExcludedProcess);
        RemoveExcludedProcessCommand = new RelayCommand(param => RemoveExcludedProcess(param as string));
    }

    public void RefreshMonitors()
    {
        MonitorPresets.Clear();
        var monitors = MultiMonitorHelper.GetAllMonitors();
        int idx = 1;
        foreach (var mon in monitors)
        {
            string dev = mon.DeviceName;
            string displayTitle = $"Монитор {idx++} ({(mon.IsPrimary ? "Основной, " : "")}{(int)mon.MonitorBounds.Width}x{(int)mon.MonitorBounds.Height})";
            string currentPreset = Settings.MonitorPresets.TryGetValue(dev, out var p) ? p : Settings.DefaultPreset.ToString();

            var item = new MonitorPresetItem(dev, displayTitle, currentPreset);
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MonitorPresetItem.SelectedPresetName))
                {
                    Settings.MonitorPresets[item.DeviceName] = item.SelectedPresetName;
                    Save();
                }
            };
            MonitorPresets.Add(item);
        }
    }

    public bool IsShiftTriggerMode
    {
        get => Settings.TriggerMode == SnapTriggerMode.ShiftDrag;
        set
        {
            if (value)
            {
                Settings.TriggerMode = SnapTriggerMode.ShiftDrag;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsAlwaysTriggerMode));
                Save();
            }
        }
    }

    public bool IsAlwaysTriggerMode
    {
        get => Settings.TriggerMode == SnapTriggerMode.AlwaysOnDrag;
        set
        {
            if (value)
            {
                Settings.TriggerMode = SnapTriggerMode.AlwaysOnDrag;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsShiftTriggerMode));
                Save();
            }
        }
    }

    public int SelectedPresetIndex
    {
        get => (int)Settings.DefaultPreset;
        set
        {
            if (value >= 0 && value < PresetOptions.Count)
            {
                Settings.DefaultPreset = (ZoneLayoutPreset)value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public int ZoneSpacing
    {
        get => Settings.ZoneSpacing;
        set
        {
            if (Settings.ZoneSpacing != value)
            {
                Settings.ZoneSpacing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ZoneSpacingText));
                Save();
            }
        }
    }

    public string ZoneSpacingText => $"{Settings.ZoneSpacing} px";

    public int ScreenMargin
    {
        get => Settings.ScreenMargin;
        set
        {
            if (Settings.ScreenMargin != value)
            {
                Settings.ScreenMargin = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ScreenMarginText));
                Save();
            }
        }
    }

    public string ScreenMarginText => $"{Settings.ScreenMargin} px";

    public bool RestoreOriginalSizeOnUnsnap
    {
        get => Settings.RestoreOriginalSizeOnUnsnap;
        set
        {
            if (Settings.RestoreOriginalSizeOnUnsnap != value)
            {
                Settings.RestoreOriginalSizeOnUnsnap = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    // Custom Layout Fields
    public int CustomColumns
    {
        get => Settings.CustomLayouts.FirstOrDefault()?.Columns ?? 2;
        set
        {
            var profile = GetOrCreateFirstCustomProfile();
            if (profile.Columns != value && value >= 1 && value <= 6)
            {
                profile.Columns = value;
                profile.ColumnWeights = Enumerable.Repeat(1.0, value).ToList();
                OnPropertyChanged();
                Save();
            }
        }
    }

    public int CustomRows
    {
        get => Settings.CustomLayouts.FirstOrDefault()?.Rows ?? 1;
        set
        {
            var profile = GetOrCreateFirstCustomProfile();
            if (profile.Rows != value && value >= 1 && value <= 4)
            {
                profile.Rows = value;
                profile.RowWeights = Enumerable.Repeat(1.0, value).ToList();
                OnPropertyChanged();
                Save();
            }
        }
    }

    public string NewProcessName
    {
        get => _newProcessName;
        set => SetProperty(ref _newProcessName, value);
    }

    private CustomLayoutProfile GetOrCreateFirstCustomProfile()
    {
        if (Settings.CustomLayouts.Count == 0)
        {
            Settings.CustomLayouts.Add(new CustomLayoutProfile());
        }
        return Settings.CustomLayouts[0];
    }

    private void AddExcludedProcess()
    {
        if (string.IsNullOrWhiteSpace(NewProcessName)) return;
        string name = NewProcessName.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        if (!Settings.ExcludedProcesses.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            Settings.ExcludedProcesses.Add(name);
            ExcludedProcesses.Add(name);
            NewProcessName = string.Empty;
            Save();
        }
    }

    private void RemoveExcludedProcess(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        Settings.ExcludedProcesses.Remove(name);
        ExcludedProcesses.Remove(name);
        Save();
    }

    private void Save()
    {
        _settingsService.Save();
    }
}
