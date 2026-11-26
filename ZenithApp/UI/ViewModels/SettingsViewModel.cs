using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using ZenithApp.Core.Lifecycle;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;
using ZenithApp.Core.Startup;
using ZenithApp.Modules.KeyRemap;
using ZenithApp.Modules.NextSession;
using ZenithApp.Modules.QuickLook;
using ZenithApp.Modules.Screenshot;
using ZenithApp.Modules.SmartLinks;
using ZenithApp.Modules.SnapZones;
using ZenithApp.Modules.StartupManager;
using ZenithApp.Modules.StickyNotes;
using ZenithApp.UI.Common;

namespace ZenithApp.UI.ViewModels;

public sealed class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IAutoLaunchService _autoLaunchService;
    private readonly IElevationService _elevationService;
    private readonly ModuleManager _moduleManager;
    private readonly ScreenshotModule? _screenshotModule;
    private readonly SmartLinksModule? _smartLinksModule;
    private readonly KeyRemapModule? _keyRemapModule;
    private readonly QuickLookModule? _quickLookModule;
    private readonly SnapZonesModule? _snapZonesModule;
    private readonly ZenithApp.Modules.RadialSwitcher.RadialSwitcherModule? _radialSwitcherModule;
    private readonly StartupManagerModule? _startupManagerModule;
    private readonly IStartupItemsService? _startupItemsService;
    private readonly NextSessionModule? _nextSessionModule;
    private readonly StickyNotesModule? _stickyNotesModule;

    private bool _startWithWindows;
    private bool _isAlwaysRunAsAdmin;
    private int _selectedThemeIndex; // 0 = System, 1 = Light, 2 = Dark
    private object? _selectedNavigationItem;

    public SettingsViewModel(
        ISettingsService settingsService,
        IAutoLaunchService autoLaunchService,
        ModuleManager moduleManager,
        ScreenshotModule? screenshotModule = null,
        SmartLinksModule? smartLinksModule = null,
        KeyRemapModule? keyRemapModule = null,
        QuickLookModule? quickLookModule = null,
        SnapZonesModule? snapZonesModule = null,
        ZenithApp.Modules.RadialSwitcher.RadialSwitcherModule? radialSwitcherModule = null,
        StartupManagerModule? startupManagerModule = null,
        IStartupItemsService? startupItemsService = null,
        IElevationService? elevationService = null,
        NextSessionModule? nextSessionModule = null,
        StickyNotesModule? stickyNotesModule = null)
    {
        _settingsService = settingsService;
        _autoLaunchService = autoLaunchService;
        _elevationService = elevationService ?? new ElevationService();
        _moduleManager = moduleManager;
        _screenshotModule = screenshotModule;
        _smartLinksModule = smartLinksModule;
        _keyRemapModule = keyRemapModule;
        _quickLookModule = quickLookModule;
        _snapZonesModule = snapZonesModule;
        _radialSwitcherModule = radialSwitcherModule;
        _startupManagerModule = startupManagerModule;
        _startupItemsService = startupItemsService;
        _nextSessionModule = nextSessionModule;
        _stickyNotesModule = stickyNotesModule;

        _startWithWindows = _autoLaunchService.IsAutoLaunchEnabled();
        if (_settingsService.Current.StartWithWindows != _startWithWindows)
        {
            _settingsService.Current.StartWithWindows = _startWithWindows;
            _settingsService.Save();
        }

        _autoLaunchService.AutoLaunchChanged += OnAutoLaunchChanged;
        _isAlwaysRunAsAdmin = _elevationService.IsAlwaysRunAsAdminEnabled();
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

        if (_quickLookModule != null)
        {
            QuickLookSettings = new QuickLookSettingsViewModel(_quickLookModule, _settingsService);
        }

        if (_snapZonesModule != null)
        {
            SnapZonesSettings = new SnapZonesSettingsViewModel(_snapZonesModule, _settingsService);
        }

        if (_radialSwitcherModule != null)
        {
            RadialSwitcherSettings = new ZenithApp.Modules.RadialSwitcher.RadialSwitcherSettingsViewModel(_radialSwitcherModule, _settingsService);
        }

        if (_startupManagerModule != null && _startupItemsService != null)
        {
            StartupManagerSettings = new StartupManagerViewModel(_startupManagerModule, _startupItemsService);
        }

        if (_nextSessionModule != null)
        {
            NextSessionSettings = new NextSessionSettingsViewModel(_nextSessionModule, _settingsService);
        }

        if (_stickyNotesModule != null)
        {
            StickyNotesSettings = new StickyNotesSettingsViewModel(_stickyNotesModule, _settingsService);
        }

        OpenSettingsFolderCommand = new RelayCommand(OpenSettingsFolder);
        RestartAsAdminCommand = new RelayCommand(RestartAsAdmin);
    }

    public ScreenshotSettingsViewModel? ScreenshotSettings { get; }
    public SmartLinksSettingsViewModel? SmartLinksSettings { get; }
    public KeyRemapSettingsViewModel? KeyRemapSettings { get; }
    public QuickLookSettingsViewModel? QuickLookSettings { get; }
    public SnapZonesSettingsViewModel? SnapZonesSettings { get; }
    public ZenithApp.Modules.RadialSwitcher.RadialSwitcherSettingsViewModel? RadialSwitcherSettings { get; }
    public StartupManagerViewModel? StartupManagerSettings { get; }
    public NextSessionSettingsViewModel? NextSessionSettings { get; }
    public StickyNotesSettingsViewModel? StickyNotesSettings { get; }

    public ObservableCollection<ModuleItemViewModel> Modules { get; }

    public ICommand OpenSettingsFolderCommand { get; }
    public ICommand RestartAsAdminCommand { get; }

    public bool IsRunningAsAdmin => _elevationService.IsRunningAsAdministrator();

    public bool IsAlwaysRunAsAdmin
    {
        get => _isAlwaysRunAsAdmin;
        set
        {
            if (SetProperty(ref _isAlwaysRunAsAdmin, value))
            {
                _elevationService.SetAlwaysRunAsAdmin(value);
            }
        }
    }

    public string AdminStatusText => IsRunningAsAdmin
        ? (Application.Current?.TryFindResource("General_Admin_StatusElevated") as string ?? "Администратор")
        : (Application.Current?.TryFindResource("General_Admin_StatusStandard") as string ?? "Обычный пользователь");

    public string AdminDescriptionText => IsRunningAsAdmin
        ? (Application.Current?.TryFindResource("General_Admin_DescElevated") as string ?? "Приложение запущено с повышенными правами.")
        : (Application.Current?.TryFindResource("General_Admin_DescStandard") as string ?? "Приложение запущено с обычными правами. Windows UIPI блокирует перехват клавиши Tab над окнами с правами администратора.");

    public Brush AdminBadgeBackground => IsRunningAsAdmin
        ? new SolidColorBrush(Color.FromArgb(40, 16, 185, 129))
        : new SolidColorBrush(Color.FromArgb(40, 245, 158, 11));

    public Brush AdminBadgeForeground => IsRunningAsAdmin
        ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
        : new SolidColorBrush(Color.FromRgb(245, 158, 11));

    public Visibility RestartAdminButtonVisibility => IsRunningAsAdmin ? Visibility.Collapsed : Visibility.Visible;

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
                SyncStartupManagerItem(value);
            }
        }
    }

    private void OnAutoLaunchChanged(bool enabled)
    {
        if (_startWithWindows != enabled)
        {
            _startWithWindows = enabled;
            OnPropertyChanged(nameof(StartWithWindows));
            SyncStartupManagerItem(enabled);
        }
    }

    private void SyncStartupManagerItem(bool enabled)
    {
        if (StartupManagerSettings == null) return;

        var zenithItem = StartupManagerSettings.Items.FirstOrDefault(i =>
            string.Equals(i.Name, "ZenithApp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(i.Name, "ZenithUtility", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(i.FilePath) && i.FilePath.EndsWith("ZenithApp.exe", StringComparison.OrdinalIgnoreCase)));

        if (zenithItem != null && zenithItem.IsEnabled != enabled)
        {
            zenithItem.IsEnabled = enabled;
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
            _ => IsSystemDarkTheme() ? ApplicationTheme.Dark : ApplicationTheme.Light
        };

        ApplicationThemeManager.Apply(targetTheme);
    }

    public static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                var appsUseLightTheme = key.GetValue("AppsUseLightTheme");
                if (appsUseLightTheme is int appsVal)
                {
                    return appsVal == 0;
                }

                var systemUsesLightTheme = key.GetValue("SystemUsesLightTheme");
                if (systemUsesLightTheme is int sysVal)
                {
                    return sysVal == 0;
                }
            }
        }
        catch
        {
            // Fallback to WPF-UI detection if registry access fails
        }

        try
        {
            var sysTheme = ApplicationThemeManager.GetSystemTheme();
            return sysTheme is SystemTheme.Dark or SystemTheme.CapturedMotion or SystemTheme.Glow or SystemTheme.HCBlack or SystemTheme.HC1 or SystemTheme.HC2;
        }
        catch
        {
            return false;
        }
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

    private void RestartAsAdmin()
    {
        _elevationService.RestartAsAdministrator();
    }

    public void Dispose()
    {
        _autoLaunchService.AutoLaunchChanged -= OnAutoLaunchChanged;
        NextSessionSettings?.Dispose();
        StickyNotesSettings?.Dispose();
    }
}
