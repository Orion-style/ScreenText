using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Settings;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.Screenshot;

public sealed class ScreenshotSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly ScreenshotModule _module;
    private readonly ISettingsService _settingsService;

    private string _screenshotHotkeyText = string.Empty;
    private string _ocrHotkeyText = string.Empty;
    private bool _saveToFile;
    private string _saveFolderPath = string.Empty;
    private bool _twoStepCaptureEnabled;
    private bool _captureOnFirstCall;
    private bool _twoStepResetAfterCapture;
    private bool _magnifierEnabled;
    private int _magnifierZoom;
    private int _magnifierGridSize;
    private bool _magnifierShowColorInfo;
    private string? _screenshotConflictMessage;
    private string? _ocrConflictMessage;

    public ScreenshotSettingsViewModel(ScreenshotModule module, ISettingsService settingsService)
    {
        _module = module;
        _settingsService = settingsService;

        LoadFromSettings();
        _module.HotkeyStatusChanged += UpdateConflictMessages;
        _module.SavedAreaChanged += UpdateSavedAreaStatus;
        UpdateConflictMessages();

        BrowseFolderCommand = new RelayCommand(BrowseFolder);
        ResetSavedAreaCommand = new RelayCommand(() => _module.ResetSavedArea(notifyUser: true));
    }

    public ICommand BrowseFolderCommand { get; }
    public ICommand ResetSavedAreaCommand { get; }

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

    public bool TwoStepCaptureEnabled
    {
        get => _twoStepCaptureEnabled;
        set
        {
            if (SetProperty(ref _twoStepCaptureEnabled, value))
            {
                _settingsService.Current.Screenshot.TwoStepCaptureEnabled = value;
                _settingsService.Save();
                UpdateSavedAreaStatus();
            }
        }
    }

    public bool CaptureOnFirstCall
    {
        get => _captureOnFirstCall;
        set
        {
            if (SetProperty(ref _captureOnFirstCall, value))
            {
                _settingsService.Current.Screenshot.CaptureOnFirstCall = value;
                _settingsService.Save();
            }
        }
    }

    public bool TwoStepResetAfterCapture
    {
        get => _twoStepResetAfterCapture;
        set
        {
            if (SetProperty(ref _twoStepResetAfterCapture, value))
            {
                _settingsService.Current.Screenshot.TwoStepResetAfterCapture = value;
                _settingsService.Save();
            }
        }
    }

    public bool MagnifierEnabled
    {
        get => _magnifierEnabled;
        set
        {
            if (SetProperty(ref _magnifierEnabled, value))
            {
                _settingsService.Current.Screenshot.MagnifierEnabled = value;
                _settingsService.Save();
            }
        }
    }

    public int MagnifierZoom
    {
        get => _magnifierZoom;
        set
        {
            int clamped = Math.Clamp(value, 2, 24);
            if (SetProperty(ref _magnifierZoom, clamped))
            {
                _settingsService.Current.Screenshot.MagnifierZoom = clamped;
                _settingsService.Save();
            }
        }
    }

    public int MagnifierGridSize
    {
        get => _magnifierGridSize;
        set
        {
            int clamped = Math.Clamp(value % 2 == 0 ? value + 1 : value, 5, 25);
            if (SetProperty(ref _magnifierGridSize, clamped))
            {
                _settingsService.Current.Screenshot.MagnifierGridSize = clamped;
                _settingsService.Save();
            }
        }
    }

    public bool MagnifierShowColorInfo
    {
        get => _magnifierShowColorInfo;
        set
        {
            if (SetProperty(ref _magnifierShowColorInfo, value))
            {
                _settingsService.Current.Screenshot.MagnifierShowColorInfo = value;
                _settingsService.Save();
            }
        }
    }

    public bool HasSavedArea => _module.HasSavedArea;

    public string SavedAreaStatusText
    {
        get
        {
            if (!_module.HasSavedArea || _module.SavedCropArea == null)
            {
                return "Текущее состояние: Область не сохранена (при следующем вызове откроется выбор).";
            }

            var area = _module.SavedCropArea.Value;
            return $"Текущее состояние: Зафиксирована область {area.Width}×{area.Height} (X={area.X}, Y={area.Y}).";
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
        TwoStepCaptureEnabled = config.TwoStepCaptureEnabled;
        CaptureOnFirstCall = config.CaptureOnFirstCall;
        TwoStepResetAfterCapture = config.TwoStepResetAfterCapture;
        MagnifierEnabled = config.MagnifierEnabled;
        MagnifierZoom = config.MagnifierZoom;
        MagnifierGridSize = config.MagnifierGridSize;
        MagnifierShowColorInfo = config.MagnifierShowColorInfo;
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

    private void UpdateSavedAreaStatus()
    {
        OnPropertyChanged(nameof(HasSavedArea));
        OnPropertyChanged(nameof(SavedAreaStatusText));
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

    public void Dispose()
    {
        _module.HotkeyStatusChanged -= UpdateConflictMessages;
        _module.SavedAreaChanged -= UpdateSavedAreaStatus;
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
