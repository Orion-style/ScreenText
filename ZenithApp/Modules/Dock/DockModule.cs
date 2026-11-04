using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;

namespace ZenithApp.Modules.Dock;

public sealed class DockModule : BaseFeatureModule
{
    public const string ModuleId = "dock";

    public DockModule(IAppLogger logger) : base(logger)
    {
    }

    public override string Id => ModuleId;
    public override string Name => "Док-панель (macOS style)";
    public override string Description => "Полупрозрачная скруглённая панель по центру экрана для быстрого запуска и переключения окон.";

    protected override Task OnEnableAsync()
    {
        // Placeholder: не создаем окно до момента использования
        Logger.LogInfo("[DockModule] Инициализирован. Графическое окно и хуки будут созданы лениво.");
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        // Placeholder: полное освобождение всех визуальных элементов
        Logger.LogInfo("[DockModule] Остановлен. Все ресурсы освобождены.");
        return Task.CompletedTask;
    }
}
