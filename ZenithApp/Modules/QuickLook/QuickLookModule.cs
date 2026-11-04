using System.Windows;
using ZenithApp.Core.Input;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;

namespace ZenithApp.Modules.QuickLook;

public sealed class QuickLookModule : BaseFeatureModule
{
    public const string ModuleId = "quick_look";
    private const int VK_SPACE = 0x20;
    private const int VK_ESCAPE = 0x1B;

    private readonly IKeyboardHookService _hookService;
    private readonly ISettingsService _settingsService;
    private IDisposable? _hookSubscription;
    private QuickLookWindow? _activePreviewWindow;
    private readonly object _previewLock = new();

    public QuickLookModule(IAppLogger logger, IKeyboardHookService hookService, ISettingsService settingsService) 
        : base(logger)
    {
        _hookService = hookService;
        _settingsService = settingsService;
    }

    public override string Id => ModuleId;
    public override string Name => "Quick Look (просмотр по Пробелу)";
    public override string Description => "Мгновенный предпросмотр изображений, PDF, аудио, видео и текстовых файлов по нажатию пробела в Проводнике или на Рабочем столе.";

    public QuickLookSettings Settings => _settingsService.Current.QuickLook;

    protected override Task OnEnableAsync()
    {
        Logger.LogInfo("[QuickLookModule] Включение модуля. Регистрация низкоуровневого хука клавиатуры...");
        _hookSubscription = _hookService.Subscribe(OnLowLevelKeyboardInput);
        Logger.LogInfo("[QuickLookModule] Модуль успешно запущен.");
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        Logger.LogInfo("[QuickLookModule] Отключение модуля. Снятие хука и закрытие активных окон...");
        _hookSubscription?.Dispose();
        _hookSubscription = null;

        CloseActivePreview();
        Logger.LogInfo("[QuickLookModule] Модуль остановлен.");
        return Task.CompletedTask;
    }

    private bool OnLowLevelKeyboardInput(KeyboardHookEventArgs e)
    {
        // 1. Быстрый фильтр: интересует только пробел (VK_SPACE) и Esc (VK_ESCAPE)
        if (e.VirtualCode != VK_SPACE && e.VirtualCode != VK_ESCAPE)
        {
            return false;
        }

        // Игнорируем синтетические события ввода, сгенерированные самим приложением
        if (e.IsInjected || e.ExtraInfo == KeyboardHookService.ExtraInfoAppMarker)
        {
            return false;
        }

        // Если окно предпросмотра активно/открыто — закрываем его по Esc или повторному Пробелу
        lock (_previewLock)
        {
            if (_activePreviewWindow != null)
            {
                if (e.IsKeyDown)
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        CloseActivePreview();
                    });
                }
                return true; // Подавляем клавишу, чтобы закрытие отработало чисто
            }
        }

        // Если окно НЕ открыто, Esc нас не интересует — отдаем его системе
        if (e.VirtualCode == VK_ESCAPE)
        {
            return false;
        }

        // Обрабатываем только KeyDown (не KeyUp)
        if (!e.IsKeyDown)
        {
            return false;
        }

        // 2. Дешёвая проверка модификаторов: если зажат Ctrl, Shift, Alt или Win, пробел не перехватываем
        if (ExplorerSelectionService.AreModifiersPressed())
        {
            return false;
        }

        // 3. Дешёвая проверка активного окна и фокуса (внутри колбэка хука, без COM и без IO)
        if (!ExplorerSelectionService.IsExplorerOrDesktopActiveFast())
        {
            return false;
        }

        // 4. Запускаем поиск выделенного файла вне горячего пути хука
        var foregroundHwnd = ExplorerSelectionService.GetForegroundWindow();

        Task.Run(() =>
        {
            try
            {
                string? filePath = ExplorerSelectionService.GetSelectedFilePath();
                if (!string.IsNullOrEmpty(filePath))
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        ShowPreview(filePath, foregroundHwnd);
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[QuickLookModule] Ошибка при определении выделенного файла: {ex.Message}", ex);
            }
        });

        // Подавляем пробел в Проводнике, чтобы он не прокручивал список файлов и не менял выделение
        return true;
    }

    private void ShowPreview(string filePath, nint foregroundHwnd)
    {
        lock (_previewLock)
        {
            CloseActivePreview();

            try
            {
                _activePreviewWindow = new QuickLookWindow(filePath, Settings, Logger, foregroundHwnd);
                _activePreviewWindow.Closed += (_, _) =>
                {
                    lock (_previewLock)
                    {
                        _activePreviewWindow = null;
                    }
                };
                _activePreviewWindow.Show();
                _activePreviewWindow.Activate();
            }
            catch (Exception ex)
            {
                Logger.LogError($"[QuickLookModule] Ошибка отображения окна предпросмотра: {ex.Message}", ex);
            }
        }
    }

    private void CloseActivePreview()
    {
        lock (_previewLock)
        {
            if (_activePreviewWindow != null)
            {
                _activePreviewWindow.CloseWindowCleanly();
                _activePreviewWindow = null;
            }
        }
    }
}
