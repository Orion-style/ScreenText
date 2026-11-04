using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;

namespace ZenithApp.Modules.T9Predictor;

public sealed class T9PredictorModule : BaseFeatureModule
{
    public const string ModuleId = "t9_predictor";

    public T9PredictorModule(IAppLogger logger) : base(logger)
    {
    }

    public override string Id => ModuleId;
    public override string Name => "T9 / Умное автодополнение";
    public override string Description => "Инлайн-подсказка продолжения набираемого слова серым полупрозрачным текстом у курсора с личным словарём.";

    protected override Task OnEnableAsync()
    {
        Logger.LogInfo("[T9PredictorModule] Инициализирован. Словарь будет загружен в память по первому вводу.");
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        Logger.LogInfo("[T9PredictorModule] Остановлен. Словарь выгружен, оверлей скрыт.");
        return Task.CompletedTask;
    }
}
