using ZenithApp.Core.Settings;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.SmartLinks;

public sealed class SmartLinksSettingsViewModel : ViewModelBase
{
    private readonly SmartLinksModule _module;
    private readonly ISettingsService _settingsService;
    private readonly Screenshot.ScreenshotModule? _screenshotModule;

    public SmartLinksSettingsViewModel(
        SmartLinksModule module,
        ISettingsService settingsService,
        Screenshot.ScreenshotModule? screenshotModule)
    {
        _module = module;
        _settingsService = settingsService;
        _screenshotModule = screenshotModule;
    }

    public bool IsScreenshotModuleEnabled => _screenshotModule?.IsEnabled == true;

    public bool AutoOpenUrls
    {
        get => _settingsService.Current.SmartLinks.AutoOpenUrls;
        set
        {
            if (_settingsService.Current.SmartLinks.AutoOpenUrls != value)
            {
                _settingsService.Current.SmartLinks.AutoOpenUrls = value;
                _settingsService.Save();
                OnPropertyChanged();
            }
        }
    }

    public bool AutoOpenQrUrls
    {
        get => _settingsService.Current.SmartLinks.AutoOpenQrUrls;
        set
        {
            if (_settingsService.Current.SmartLinks.AutoOpenQrUrls != value)
            {
                _settingsService.Current.SmartLinks.AutoOpenQrUrls = value;
                _settingsService.Save();
                OnPropertyChanged();
            }
        }
    }

    public bool ShowPopupForPlainText
    {
        get => _settingsService.Current.SmartLinks.ShowPopupForPlainText;
        set
        {
            if (_settingsService.Current.SmartLinks.ShowPopupForPlainText != value)
            {
                _settingsService.Current.SmartLinks.ShowPopupForPlainText = value;
                _settingsService.Save();
                OnPropertyChanged();
            }
        }
    }

    public int PopupTimeoutSeconds
    {
        get => _settingsService.Current.SmartLinks.PopupTimeoutSeconds;
        set
        {
            if (_settingsService.Current.SmartLinks.PopupTimeoutSeconds != value)
            {
                _settingsService.Current.SmartLinks.PopupTimeoutSeconds = Math.Clamp(value, 3, 60);
                _settingsService.Save();
                OnPropertyChanged();
            }
        }
    }

    public string DefaultTranslateLanguage
    {
        get => _settingsService.Current.SmartLinks.DefaultTranslateLanguage;
        set
        {
            if (_settingsService.Current.SmartLinks.DefaultTranslateLanguage != value)
            {
                _settingsService.Current.SmartLinks.DefaultTranslateLanguage = value;
                _settingsService.Save();
                OnPropertyChanged();
            }
        }
    }

    public void RefreshState()
    {
        OnPropertyChanged(nameof(IsScreenshotModuleEnabled));
        OnPropertyChanged(nameof(AutoOpenUrls));
        OnPropertyChanged(nameof(AutoOpenQrUrls));
        OnPropertyChanged(nameof(ShowPopupForPlainText));
        OnPropertyChanged(nameof(PopupTimeoutSeconds));
        OnPropertyChanged(nameof(DefaultTranslateLanguage));
    }
}
