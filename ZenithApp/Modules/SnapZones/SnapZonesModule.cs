using System;
using System.Linq;
using System.Threading.Tasks;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;

namespace ZenithApp.Modules.SnapZones;

public sealed class SnapZonesModule : BaseFeatureModule
{
    public const string ModuleId = "snap_zones";

    private readonly IHotkeyService _hotkeyService;
    private readonly ISettingsService _settingsService;

    private WindowMoveTracker? _tracker;
    private SnapHotkeyController? _hotkeyController;

    public SnapZonesModule(
        IAppLogger logger, 
        IHotkeyService hotkeyService, 
        ISettingsService settingsService) 
        : base(logger)
    {
        _hotkeyService = hotkeyService;
        _settingsService = settingsService;
    }

    public override string Id => ModuleId;
    public override string Name => "Закрепление окон по зонам";
    public override string Description => "Разделение экранов на настраиваемые зоны (FancyZones) с привязкой окон при перетаскивании с Shift или по горячим клавишам.";

    public SnapZonesSettings Settings => _settingsService.Current.SnapZones;

    protected override Task OnEnableAsync()
    {
        Logger.LogInfo("[SnapZonesModule] Включение модуля. Регистрация трекера перемещения окон и горячих клавиш...");

        _tracker = new WindowMoveTracker(
            Logger,
            Settings,
            () => Settings.DefaultPreset,
            () => Settings.CustomLayouts.FirstOrDefault());

        _tracker.StartTracking();

        _hotkeyController = new SnapHotkeyController(
            _hotkeyService,
            Logger,
            Settings,
            () => Settings.DefaultPreset,
            () => Settings.CustomLayouts.FirstOrDefault());

        _hotkeyController.RegisterHotkeys();

        Logger.LogInfo("[SnapZonesModule] Модуль успешно запущен.");
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        Logger.LogInfo("[SnapZonesModule] Отключение модуля. Освобождение ресурсов и хуков...");

        _hotkeyController?.Dispose();
        _hotkeyController = null;

        _tracker?.Dispose();
        _tracker = null;

        Logger.LogInfo("[SnapZonesModule] Модуль остановлен.");
        return Task.CompletedTask;
    }
}
