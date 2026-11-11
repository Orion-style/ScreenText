using System.Threading.Tasks;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;

namespace ZenithApp.Modules.StartupManager;

public sealed class StartupManagerModule : BaseFeatureModule
{
    public const string ModuleId = "startup_manager";

    public StartupManagerModule(IAppLogger logger) : base(logger)
    {
    }

    public override string Id => ModuleId;
    public override string Name => "Управление автозагрузкой";
    public override string Description => "Просмотр установленных программ в автозагрузке Windows, а также их быстрое включение и выключение.";

    protected override Task OnEnableAsync()
    {
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        return Task.CompletedTask;
    }
}
