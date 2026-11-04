using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Lifecycle;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;
using ZenithApp.Core.Startup;
using ZenithApp.Modules.KeyRemap;
using ZenithApp.Modules.QuickLook;
using ZenithApp.Modules.Screenshot;
using ZenithApp.Modules.SmartLinks;
using ZenithApp.Modules.SnapZones;
using ZenithApp.UI.Tray;
using ZenithApp.UI.ViewModels;
using ZenithApp.UI.Views;

namespace ZenithApp;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;
    private ISingleInstanceService? _singleInstanceService;
    private TrayIconService? _trayIconService;
    private SettingsWindow? _settingsWindow;
    private readonly object _windowLock = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Инициализация DI-контейнера
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var logger = _serviceProvider.GetRequiredService<IAppLogger>();
        logger.LogInfo("=== Приложение Zenith Utility запускается ===");

        // 2. Проверка Mutex (один экземпляр)
        _singleInstanceService = new SingleInstanceService("ZenithUtility", logger);
        if (!_singleInstanceService.IsFirstInstance)
        {
            logger.LogInfo("Обнаружен уже запущенный экземпляр приложения. Отправка сигнала активации и выход.");
            _singleInstanceService.NotifyFirstInstance();
            Shutdown(0);
            return;
        }

        // Прослушивание повторных запусков в фоне
        _singleInstanceService.StartListening(() =>
        {
            Dispatcher.Invoke(() =>
            {
                OpenSettingsWindow();
            });
        });

        // 3. Применение темы оформления
        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        SettingsViewModel.ApplyTheme(settingsService.Current.Theme);

        // 4. Инициализация трея
        _trayIconService = new TrayIconService(
            logger,
            openSettingsAction: () => Dispatcher.Invoke(OpenSettingsWindow),
            exitAction: () => Dispatcher.Invoke(ExitApplication)
        );
        _trayIconService.Initialize();

        // 5. Инициализация модулей (выключенные модули не занимают ресурсы)
        var moduleManager = _serviceProvider.GetRequiredService<ModuleManager>();
        await moduleManager.InitializeModulesAsync();

        // 6. Если запуск НЕ тихий (без флага --minimized), открываем окно настроек
        var isMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        if (!isMinimized)
        {
            OpenSettingsWindow();
        }

        // Сборка мусора после старта для достижения минимального Working Set памяти
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Core Services
        services.AddSingleton<IAppLogger, FileLogger>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IAutoLaunchService, AutoLaunchService>();
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<ZenithApp.Core.Input.IKeyboardHookService, ZenithApp.Core.Input.KeyboardHookService>();

        // Feature Modules
        services.AddSingleton<ScreenshotModule>();
        services.AddSingleton<IFeatureModule>(sp => sp.GetRequiredService<ScreenshotModule>());
        services.AddSingleton<SmartLinksModule>();
        services.AddSingleton<IFeatureModule>(sp => sp.GetRequiredService<SmartLinksModule>());
        services.AddSingleton<ITextRecognizedHandler>(sp => sp.GetRequiredService<SmartLinksModule>());
        services.AddSingleton<KeyRemapModule>();
        services.AddSingleton<IFeatureModule>(sp => sp.GetRequiredService<KeyRemapModule>());
        services.AddSingleton<QuickLookModule>();
        services.AddSingleton<IFeatureModule>(sp => sp.GetRequiredService<QuickLookModule>());
        services.AddSingleton<SnapZonesModule>();
        services.AddSingleton<IFeatureModule>(sp => sp.GetRequiredService<SnapZonesModule>());

        // Module Manager
        services.AddSingleton<ModuleManager>();

        // UI ViewModels
        services.AddTransient<SettingsViewModel>();
    }

    public void OpenSettingsWindow()
    {
        lock (_windowLock)
        {
            if (_settingsWindow != null)
            {
                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }
                _settingsWindow.Activate();
                _settingsWindow.Focus();
                return;
            }

            var vm = _serviceProvider!.GetRequiredService<SettingsViewModel>();
            _settingsWindow = new SettingsWindow(vm);

            // Закрытие окна настроек не завершает приложение, а полностью выгружает окно из памяти
            _settingsWindow.Closed += (_, _) =>
            {
                lock (_windowLock)
                {
                    _settingsWindow = null;
                }

                // Агрессивное освобождение памяти после закрытия окна GUI
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                TrimProcessMemory();
            };

            _settingsWindow.Show();
            _settingsWindow.Activate();
        }
    }

    public void ExitApplication()
    {
        var logger = _serviceProvider?.GetService<IAppLogger>();
        logger?.LogInfo("Завершение работы приложения по запросу пользователя.");

        _trayIconService?.Dispose();
        _singleInstanceService?.Dispose();

        if (_serviceProvider is IDisposable disposableProvider)
        {
            disposableProvider.Dispose();
        }

        Shutdown(0);
    }

    private static void TrimProcessMemory()
    {
        try
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            SetProcessWorkingSetSize(proc.Handle, -1, -1);
        }
        catch
        {
            // Optional win32 memory compaction
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);
}
