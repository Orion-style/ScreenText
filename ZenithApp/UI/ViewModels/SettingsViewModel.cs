using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Wpf.Ui.Appearance;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;
using ZenithApp.Core.Startup;
using ZenithApp.Modules.Dock;
using ZenithApp.Modules.KeyRemap;
using ZenithApp.Modules.Screenshot;
using ZenithApp.Modules.SmartLinks;
using ZenithApp.UI.Common;

namespace ZenithApp.UI.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IAutoLaunchService _autoLaunchService;
    private readonly ModuleManager _moduleManager;
    private readonly ScreenshotModule? _screenshotModule;
    private readonly SmartLinksModule? _smartLinksModule;
    private readonly KeyRemapModule? _keyRemapModule;
    private readonly DockModule? _dockModule;

    private bool _startWithWindows;
    private int _selectedThemeIndex; // 0 = System, 1 = Light, 2 = Dark
    private object? _selectedNavigationItem;

    public SettingsViewModel(
        ISettingsService settingsService,
        IAutoLaunchService autoLaunchService,
        ModuleManager moduleManager,
        ScreenshotModule? screenshotModule = null,
        SmartLinksModule? smartLinksModule = null,
        KeyRemapModule? keyRemapModule = null,
        DockModule? dockModule = null)
    {
        _settingsService = settingsService;
        _autoLaunchService = autoLaunchService;
        _moduleManager = moduleManager;
        _screenshotModule = screenshotModule;
        _smartLinksModule = smartLinksModule;
        _keyRemapModule = keyRemapModule;
        _dockModule = dockModule;

        _startWithWindows = _settingsService.Current.StartWithWindows;
        _selectedThemeIndex = (int)_settingsService.Current.Theme;

        Modules = new ObservableCollection<ModuleItemViewModel>(
            _moduleManager.AllModules.Select(m => new ModuleItemViewModel(m, _moduleManager))
        );

        if (_screenshotModule != null)
        {
            ScreenshotSettings = new ScreenshotSettingsViewModel(_screenshotModule, _settingsService);
        }

        if (_smartLinksModule != null)
        {
            SmartLinksSettings = new SmartLinksSettingsViewModel(_smartLinksModule, _settingsService, _screenshotModule);
        }

        if (_keyRemapModule != null)
        {
            KeyRemapSettings = new KeyRemapSettingsViewModel(_keyRemapModule, _settingsService);
        }

        if (_dockModule != null)
        {
            DockSettings = new DockSettingsViewModel(_dockModule, _settingsService);
        }

        OpenSettingsFolderCommand = new RelayCommand(OpenSettingsFolder);
    }

    public ScreenshotSettingsViewModel? ScreenshotSettings { get; }
    public SmartLinksSettingsViewModel? SmartLinksSettings { get; }
    public KeyRemapSettingsViewModel? KeyRemapSettings { get; }
    public DockSettingsViewModel? DockSettings { get; }

    public ObservableCollection<ModuleItemViewModel> Modules { get; }

    public ICommand OpenSettingsFolderCommand { get; }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetProperty(ref _startWithWindows, value))
            {
                _autoLaunchService.SetAutoLaunch(value);
                _settingsService.Current.StartWithWindows = value;
                _settingsService.Save();
            }
        }
    }

    public int SelectedThemeIndex
    {
        get => _selectedThemeIndex;
        set
        {
            if (SetProperty(ref _selectedThemeIndex, value))
            {
                var theme = (AppThemeMode)value;
                _settingsService.Current.Theme = theme;
                _settingsService.Save();
                ApplyTheme(theme);
            }
        }
    }

    public object? SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set => SetProperty(ref _selectedNavigationItem, value);
    }

    public static void ApplyTheme(AppThemeMode theme)
    {
        var targetTheme = theme switch
        {
            AppThemeMode.Light => ApplicationTheme.Light,
            AppThemeMode.Dark => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light
        };

        ApplicationThemeManager.Apply(targetTheme);
    }

    private void OpenSettingsFolder()
    {
        var folder = _settingsService.SettingsDirectory;
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
    }
}
