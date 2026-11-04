using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Settings;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.T9Predictor;

public sealed class T9PredictorSettingsViewModel : ViewModelBase
{
    private readonly T9PredictorModule _module;
    private readonly ISettingsService _settingsService;
    private string _newProcessName = string.Empty;

    public T9PredictorSettings Settings => _settingsService.Current.T9Predictor;

    public ObservableCollection<string> ExcludedProcesses { get; } = new();

    public ICommand AddExcludedProcessCommand { get; }
    public ICommand RemoveExcludedProcessCommand { get; }

    public T9PredictorSettingsViewModel(T9PredictorModule module, ISettingsService settingsService)
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

    public bool EnableRussian
    {
        get => Settings.EnableRussian;
        set
        {
            if (Settings.EnableRussian != value)
            {
                Settings.EnableRussian = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public bool EnableEnglish
    {
        get => Settings.EnableEnglish;
        set
        {
            if (Settings.EnableEnglish != value)
            {
                Settings.EnableEnglish = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public int MinPrefixLength
    {
        get => Settings.MinPrefixLength;
        set
        {
            if (Settings.MinPrefixLength != value)
            {
                Settings.MinPrefixLength = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MinPrefixLengthText));
                Save();
            }
        }
    }

    public string MinPrefixLengthText => $"{Settings.MinPrefixLength} симв.";

    public int HintOpacityPercent
    {
        get => (int)Math.Round(Settings.HintOpacity * 100);
        set
        {
            double op = value / 100.0;
            if (Math.Abs(Settings.HintOpacity - op) > 0.01)
            {
                Settings.HintOpacity = op;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HintOpacityText));
                Save();
            }
        }
    }

    public string HintOpacityText => $"{HintOpacityPercent}%";

    public int SelectedAcceptKeyIndex
    {
        get => (int)Settings.AcceptKey;
        set
        {
            if (value >= 0 && value <= 2)
            {
                Settings.AcceptKey = (AcceptKeyOption)value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public bool AddSpaceAfterWord
    {
        get => Settings.AddSpaceAfterWord;
        set
        {
            if (Settings.AddSpaceAfterWord != value)
            {
                Settings.AddSpaceAfterWord = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public string PauseHotkeyText => $"{Settings.PauseHotkeyModifiers} + {Settings.PauseHotkeyKey}";

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
        _module.RegisterPauseHotkey();
    }
}
