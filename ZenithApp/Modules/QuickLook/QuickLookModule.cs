using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;

namespace ZenithApp.Modules.QuickLook;

public sealed class QuickLookModule : BaseFeatureModule
{
    public const string ModuleId = "quick_look";

    public QuickLookModule(IAppLogger logger) : base(logger)
    {
    }

    public override string Id => ModuleId;
    public override string Name => "Quick Look (просмотр по Пробелу)";
    public override string Description => "Мгновенный предпросмотр изображений, PDF, аудио и видеофайлов по нажатию клавиши Space.";

    protected override Task OnEnableAsync()
    {
        Logger.LogInfo("[QuickLookModule] Инициализирован. Клавиатурный хук будет активен только в Проводнике.");
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        Logger.LogInfo("[QuickLookModule] Остановлен. Хук снят, предпросмотрщики закрыты.");
        return Task.CompletedTask;
    }
}
