using ZenithApp.Core.Logging;

namespace ZenithApp.Core.Modules;

public abstract class BaseFeatureModule : IFeatureModule
{
    protected readonly IAppLogger Logger;
    private bool _isEnabled;

    protected BaseFeatureModule(IAppLogger logger)
    {
        Logger = logger;
    }

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }

    public bool IsEnabled => _isEnabled;

    public async Task<bool> EnableAsync()
    {
        if (_isEnabled) return true;

        try
        {
            Logger.LogInfo($"[Module:{Id}] Enabling module '{Name}'...");
            await OnEnableAsync();
            _isEnabled = true;
            Logger.LogInfo($"[Module:{Id}] Module '{Name}' enabled successfully.");
            return true;
        }
        catch (Exception ex)
        {
            _isEnabled = false;
            Logger.LogError($"[Module:{Id}] Failed to enable module '{Name}'. Disabling it for safety.", ex);
            try
            {
                await OnDisableAsync();
            }
            catch
            {
                // Ignore cleanup errors during failure recovery
            }
            return false;
        }
    }

    public async Task DisableAsync()
    {
        if (!_isEnabled) return;

        try
        {
            Logger.LogInfo($"[Module:{Id}] Disabling module '{Name}'...");
            await OnDisableAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Module:{Id}] Error while disabling module '{Name}'.", ex);
        }
        finally
        {
            _isEnabled = false;
            Logger.LogInfo($"[Module:{Id}] Module '{Name}' disabled and resources released.");
        }
    }

    protected abstract Task OnEnableAsync();
    protected abstract Task OnDisableAsync();

    public virtual void Dispose()
    {
        if (_isEnabled)
        {
            DisableAsync().GetAwaiter().GetResult();
        }
    }
}
