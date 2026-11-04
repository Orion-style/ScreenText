using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Settings;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.SnapZones;

public sealed class SnapZonesSettingsViewModel : ViewModelBase
{
    private readonly SnapZonesModule _module;
    private readonly ISettingsService _settingsService;
    private string _newProcessName = string.Empty;

    public SnapZonesSettings Settings => _settingsService.Current.SnapZones;

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

        AddExcludedProcessCommand = new RelayCommand(AddExcludedProcess);
        RemoveExcludedProcessCommand = new RelayCommand(param => RemoveExcludedProcess(param as string));
    }

    public string HotkeyDisplayText => $"{Settings.HotkeyModifiers} + {Settings.HotkeyKey}";

    public bool ShowBorder
    {
        get => Settings.ShowBorder;
        set
        {
            if (Settings.ShowBorder != value)
            {
                Settings.ShowBorder = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public int BorderThickness
    {
        get => Settings.BorderThickness;
        set
        {
            if (Settings.BorderThickness != value)
            {
                Settings.BorderThickness = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BorderThicknessText));
                Save();
            }
        }
    }

    public string BorderThicknessText => $"{Settings.BorderThickness} px";

    public bool PlayNotificationSound
    {
        get => Settings.PlayNotificationSound;
        set
        {
            if (Settings.PlayNotificationSound != value)
            {
                Settings.PlayNotificationSound = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public string BorderColorHex
    {
        get => Settings.BorderColorHex;
        set
        {
            if (Settings.BorderColorHex != value)
            {
                Settings.BorderColorHex = value;
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
        _module.RegisterHotkey();
    }
}
