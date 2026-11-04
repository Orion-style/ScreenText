using ZenithApp.Core.Modules;
using ZenithApp.UI.Common;

namespace ZenithApp.UI.ViewModels;

public sealed class ModuleItemViewModel : ViewModelBase
{
    private readonly IFeatureModule _module;
    private readonly ModuleManager _moduleManager;
    private bool _isEnabled;
    private bool _isBusy;

    public ModuleItemViewModel(IFeatureModule module, ModuleManager moduleManager)
    {
        _module = module;
        _moduleManager = moduleManager;
        _isEnabled = module.IsEnabled;
    }

    public string Id => _module.Id;
    public string Name => _module.Name;
    public string Description => _module.Description;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                _ = ApplyStateAsync(value);
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    private async Task ApplyStateAsync(bool enable)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var success = await _moduleManager.SetModuleStateAsync(Id, enable);
            if (!success)
            {
                _isEnabled = _module.IsEnabled;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void RefreshState()
    {
        IsEnabled = _module.IsEnabled;
    }
}
