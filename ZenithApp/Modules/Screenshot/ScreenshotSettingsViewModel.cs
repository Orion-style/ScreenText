using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Settings;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.Screenshot;

public sealed class ScreenshotSettingsViewModel : ViewModelBase
{
    private readonly ScreenshotModule _module;
    private readonly ISettingsService _settingsService;

    private string _screenshotHotkeyText = string.Empty;
    private string _ocrHotkeyText = string.Empty;
    private bool _saveToFile;
    private string _saveFolderPath = string.Empty;
    private string? _screenshotConflictMessage;
    private string? _ocrConflictMessage;

    public ScreenshotSettingsViewModel(ScreenshotModule module, ISettingsService settingsService)
    {
        _module = module;
        _settingsService = settingsService;

        LoadFromSettings();
        _module.HotkeyStatusChanged += UpdateConflictMessages;
        UpdateConflictMessages();

        BrowseFolderCommand = new RelayCommand(BrowseFolder);
    }

    public ICommand BrowseFolderCommand { get; }

    public string ScreenshotHotkeyText
    {
        get => _screenshotHotkeyText;
        set => SetProperty(ref _screenshotHotkeyText, value);
    }

    public string OcrHotkeyText
    {
        get => _ocrHotkeyText;
        set => SetProperty(ref _ocrHotkeyText, value);
    }

    public bool SaveToFile
    {
        get => _saveToFile;
        set
        {
            if (SetProperty(ref _saveToFile, value))
            {
                _settingsService.Current.Screenshot.SaveToFileEnabled = value;
                _settingsService.Save();
            }
        }
    }

    public string SaveFolderPath
    {
        get => _saveFolderPath;
        set
        {
            if (SetProperty(ref _saveFolderPath, value))
            {
                _settingsService.Current.Screenshot.SaveFolderPath = value;
                _settingsService.Save();
            }
        }
    }

    public string? ScreenshotConflictMessage
    {
        get => _screenshotConflictMessage;
        set => SetProperty(ref _screenshotConflictMessage, value);
    }

    public string? OcrConflictMessage
    {
        get => _ocrConflictMessage;
        set => SetProperty(ref _ocrConflictMessage, value);
    }

    public bool HasScreenshotConflict => !string.IsNullOrEmpty(ScreenshotConflictMessage);
    public bool HasOcrConflict => !string.IsNullOrEmpty(OcrConflictMessage);

    private void LoadFromSettings()
    {
        var config = _settingsService.Current.Screenshot;
        ScreenshotHotkeyText = FormatHotkey(config.ScreenshotModifiers, config.ScreenshotKey);
        OcrHotkeyText = FormatHotkey(config.OcrModifiers, config.OcrKey);
        SaveToFile = config.SaveToFileEnabled;
        SaveFolderPath = config.SaveFolderPath;
    }

    public void UpdateScreenshotHotkey(KeyModifiers modifiers, Key key)
    {
        var config = _settingsService.Current.Screenshot;
        config.ScreenshotModifiers = modifiers;
        config.ScreenshotKey = key;
        _settingsService.Save();
        ScreenshotHotkeyText = FormatHotkey(modifiers, key);

        if (_module.IsEnabled)
        {
            _module.RegisterHotkeys();
        }
    }

    public void UpdateOcrHotkey(KeyModifiers modifiers, Key key)
    {
        var config = _settingsService.Current.Screenshot;
        config.OcrModifiers = modifiers;
        config.OcrKey = key;
        _settingsService.Save();
        OcrHotkeyText = FormatHotkey(modifiers, key);

        if (_module.IsEnabled)
        {
            _module.RegisterHotkeys();
        }
    }

    private void UpdateConflictMessages()
    {
        ScreenshotConflictMessage = _module.HotkeyScreenshotError;
        OcrConflictMessage = _module.HotkeyOcrError;
        OnPropertyChanged(nameof(HasScreenshotConflict));
        OnPropertyChanged(nameof(HasOcrConflict));
    }

    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для сохранения скриншотов",
            InitialDirectory = Directory.Exists(SaveFolderPath) ? SaveFolderPath : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };

        if (dialog.ShowDialog() == true)
        {
            SaveFolderPath = dialog.FolderName;
        }
    }

    public static string FormatHotkey(KeyModifiers modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Win)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }
}
