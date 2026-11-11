using ZenithApp.Core.Logging;
using ZenithApp.Core.Settings;

namespace ZenithApp.Core.Modules;

public sealed class ModuleManager : IDisposable
{
    private readonly IAppLogger _logger;
    private readonly ISettingsService _settingsService;
    private readonly Dictionary<string, IFeatureModule> _modules = new(StringComparer.OrdinalIgnoreCase);

    public ModuleManager(
        IAppLogger logger,
        ISettingsService settingsService,
        IEnumerable<IFeatureModule> modules)
    {
        _logger = logger;
        _settingsService = settingsService;

        foreach (var module in modules)
        {
            _modules[module.Id] = module;
        }
    }

    public IReadOnlyCollection<IFeatureModule> AllModules => _modules.Values;

    public IFeatureModule? GetModule(string id)
    {
        return _modules.TryGetValue(id, out var module) ? module : null;
    }

    public async Task InitializeModulesAsync()
    {
        _logger.LogInfo($"Initializing {_modules.Count} registered feature modules...");

        foreach (var module in _modules.Values)
        {
            var isDefaultOn = string.Equals(module.Id, "screenshot", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(module.Id, "smart_links", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(module.Id, "radial_switcher", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(module.Id, "startup_manager", StringComparison.OrdinalIgnoreCase);

            var shouldEnable = _settingsService.Current.IsModuleEnabled(module.Id, defaultValue: isDefaultOn);
            if (shouldEnable)
            {
                var success = await module.EnableAsync();
                if (!success)
                {
                    _logger.LogWarning($"Module '{module.Id}' failed to start during initialization. Updating setting to false.");
                    _settingsService.Current.SetModuleEnabled(module.Id, false);
                    _settingsService.Save();
                }
            }
            else
            {
                _logger.LogInfo($"[Module:{module.Id}] Disabled by configuration (0 resources allocated).");
            }
        }
    }

    public async Task<bool> SetModuleStateAsync(string id, bool enable)
    {
        if (!_modules.TryGetValue(id, out var module))
        {
            _logger.LogWarning($"Attempted to toggle unknown module '{id}'.");
            return false;
        }

        if (enable == module.IsEnabled)
        {
            return true;
        }

        bool result;
        if (enable)
        {
            result = await module.EnableAsync();
            if (!result)
            {
                _logger.LogWarning($"Could not enable module '{id}'. It will remain disabled.");
            }
        }
        else
        {
            await module.DisableAsync();
            result = true;
        }

        _settingsService.Current.SetModuleEnabled(id, module.IsEnabled);
        _settingsService.Save();
        return result;
    }

    public void Dispose()
    {
        foreach (var module in _modules.Values)
        {
            try
            {
                module.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error disposing module '{module.Id}'.", ex);
            }
        }
    }
}
