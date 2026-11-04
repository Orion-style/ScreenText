namespace ZenithApp.Core.Modules;

public interface IFeatureModule : IDisposable
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    bool IsEnabled { get; }
    Task<bool> EnableAsync();
    Task DisableAsync();
}
