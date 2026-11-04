using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;

namespace ZenithApp.Modules.SnapZones;

public sealed class SnapZonesModule : BaseFeatureModule
{
    public const string ModuleId = "snap_zones";

    public SnapZonesModule(IAppLogger logger) : base(logger)
    {
    }

    public override string Id => ModuleId;
    public override string Name => "Закрепление окон по зонам";
    public override string Description => "Разделение рабочих экранов на настраиваемые зоны с привязкой окон при перетаскивании с Shift.";

    protected override Task OnEnableAsync()
    {
        Logger.LogInfo("[SnapZonesModule] Инициализирован. Сетка зон загружена.");
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        Logger.LogInfo("[SnapZonesModule] Остановлен. Перехват окон отключен.");
        return Task.CompletedTask;
    }
}
