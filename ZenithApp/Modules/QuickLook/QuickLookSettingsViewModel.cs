using ZenithApp.Core.Logging;
using ZenithApp.Core.Settings;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.QuickLook;

public sealed class QuickLookSettingsViewModel : ViewModelBase
{
    private readonly QuickLookModule _module;
    private readonly ISettingsService _settingsService;

    public QuickLookSettingsViewModel(QuickLookModule module, ISettingsService settingsService)
    {
        _module = module;
        _settingsService = settingsService;
    }

    public QuickLookSettings Settings => _settingsService.Current.QuickLook;

    public bool AutoPlayMedia
    {
        get => Settings.AutoPlayMedia;
        set
        {
            if (Settings.AutoPlayMedia != value)
            {
                Settings.AutoPlayMedia = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public int MaxTextFileSizeKb
    {
        get => Settings.MaxTextFileSizeKb;
        set
        {
            if (Settings.MaxTextFileSizeKb != value)
            {
                Settings.MaxTextFileSizeKb = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MaxTextFileSizeKbText));
                Save();
            }
        }
    }

    public string MaxTextFileSizeKbText => $"{Settings.MaxTextFileSizeKb} КБ";

    public int MaxWindowScreenPercent
    {
        get => Settings.MaxWindowScreenPercent;
        set
        {
            if (Settings.MaxWindowScreenPercent != value)
            {
                Settings.MaxWindowScreenPercent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MaxWindowScreenPercentText));
                Save();
            }
        }
    }

    public string MaxWindowScreenPercentText => $"{Settings.MaxWindowScreenPercent}%";

    public bool EnableImages
    {
        get => Settings.EnableImages;
        set
        {
            if (Settings.EnableImages != value)
            {
                Settings.EnableImages = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public bool EnablePdf
    {
        get => Settings.EnablePdf;
        set
        {
            if (Settings.EnablePdf != value)
            {
                Settings.EnablePdf = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public bool EnableMedia
    {
        get => Settings.EnableMedia;
        set
        {
            if (Settings.EnableMedia != value)
            {
                Settings.EnableMedia = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public bool EnableText
    {
        get => Settings.EnableText;
        set
        {
            if (Settings.EnableText != value)
            {
                Settings.EnableText = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    private void Save()
    {
        _settingsService.Save();
    }
}
