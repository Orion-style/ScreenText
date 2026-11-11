using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.StartupManager;

public sealed class StartupItemViewModel : ViewModelBase
{
    private readonly StartupEntry _entry;
    private readonly IStartupItemsService _service;
    private readonly Action<StartupItemViewModel>? _onDeleted;
    private bool _isEnabled;

    public StartupItemViewModel(StartupEntry entry, IStartupItemsService service, Action<StartupItemViewModel>? onDeleted = null)
    {
        _entry = entry;
        _service = service;
        _onDeleted = onDeleted;
        _isEnabled = entry.IsEnabled;

        ToggleCommand = new RelayCommand(Toggle);
        OpenLocationCommand = new RelayCommand(OpenLocation, () => !string.IsNullOrEmpty(entry.FilePath) && File.Exists(entry.FilePath));
        DeleteCommand = new RelayCommand(Delete);
    }

    public string Name => _entry.Name;
    public string Command => _entry.Command;
    public string LocationDisplayName => _entry.LocationDisplayName;
    public string? Publisher => string.IsNullOrWhiteSpace(_entry.Publisher) ? "Неизвестный издатель" : _entry.Publisher;
    public string? FilePath => _entry.FilePath;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                if (_service.SetStartupEntryEnabled(_entry, value))
                {
                    SetProperty(ref _isEnabled, value);
                }
            }
        }
    }

    public ICommand ToggleCommand { get; }
    public ICommand OpenLocationCommand { get; }
    public ICommand DeleteCommand { get; }

    private void Toggle()
    {
        IsEnabled = !IsEnabled;
    }

    private void OpenLocation()
    {
        if (string.IsNullOrEmpty(_entry.FilePath) || !File.Exists(_entry.FilePath)) return;

        try
        {
            Process.Start("explorer.exe", $"/select,\"{_entry.FilePath}\"");
        }
        catch
        {
            // Ignore explorer launch issues
        }
    }

    private void Delete()
    {
        if (_service.RemoveStartupEntry(_entry))
        {
            _onDeleted?.Invoke(this);
        }
    }
}
