using System.Collections.ObjectModel;
using System.Windows.Input;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.Dock;

public sealed class DockSettingsItemViewModel : ViewModelBase
{
    private readonly DockItem _item;
    private readonly DockSettingsViewModel _parent;

    public DockItem Item => _item;

    public DockSettingsItemViewModel(DockItem item, DockSettingsViewModel parent)
    {
        _item = item;
        _parent = parent;

        MoveUpCommand = new RelayCommand(() => _parent.MoveItem(this, -1));
        MoveDownCommand = new RelayCommand(() => _parent.MoveItem(this, 1));
        DeleteCommand = new RelayCommand(() => _parent.DeleteItem(this));
    }

    public string Title => _item.Title;
    public string TargetPath => _item.TargetPath;

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand DeleteCommand { get; }
}

public sealed class DockSettingsViewModel : ViewModelBase
{
    private readonly DockModule _module;
    private readonly ZenithApp.Core.Settings.ISettingsService _settingsService;

    private bool _autoHide;
    private int _bottomMargin;
    private double _iconSize;
    private bool _enableMagnification;
    private double _magnificationFactor;
    private bool _enableBounceAnimation;
    private int _selectedMonitorIndex;
    private string _hotkeyText;

    public DockSettingsViewModel(
        DockModule module,
        ZenithApp.Core.Settings.ISettingsService settingsService)
    {
        _module = module;
        _settingsService = settingsService;

        _autoHide = _module.Settings.AutoHide;
        _bottomMargin = _module.Settings.BottomMargin;
        _iconSize = _module.Settings.IconSize;
        _enableMagnification = _module.Settings.EnableMagnification;
        _magnificationFactor = _module.Settings.MagnificationFactor;
        _enableBounceAnimation = _module.Settings.EnableBounceAnimation;
        _selectedMonitorIndex = (int)_module.Settings.MonitorTarget;
        _hotkeyText = $"{_module.Settings.HotkeyModifiers} + {_module.Settings.HotkeyKey}";

        Items = new ObservableCollection<DockSettingsItemViewModel>(
            _module.Settings.Items.Select(i => new DockSettingsItemViewModel(i, this))
        );

        _module.ItemsChanged += OnModuleItemsChanged;
    }

    public ObservableCollection<DockSettingsItemViewModel> Items { get; }

    public bool AutoHide
    {
        get => _autoHide;
        set
        {
            if (SetProperty(ref _autoHide, value))
            {
                _module.Settings.AutoHide = value;
                _module.ApplySettingsChanges();
            }
        }
    }

    public int BottomMargin
    {
        get => _bottomMargin;
        set
        {
            if (SetProperty(ref _bottomMargin, value))
            {
                _module.Settings.BottomMargin = value;
                _module.ApplySettingsChanges();
            }
        }
    }

    public bool EnableMagnification
    {
        get => _enableMagnification;
        set
        {
            if (SetProperty(ref _enableMagnification, value))
            {
                _module.Settings.EnableMagnification = value;
                _module.ApplySettingsChanges();
            }
        }
    }

    public double MagnificationFactor
    {
        get => _magnificationFactor;
        set
        {
            if (SetProperty(ref _magnificationFactor, value))
            {
                _module.Settings.MagnificationFactor = value;
                _module.ApplySettingsChanges();
            }
        }
    }

    public bool EnableBounceAnimation
    {
        get => _enableBounceAnimation;
        set
        {
            if (SetProperty(ref _enableBounceAnimation, value))
            {
                _module.Settings.EnableBounceAnimation = value;
                _module.ApplySettingsChanges();
            }
        }
    }

    public int SelectedMonitorIndex
    {
        get => _selectedMonitorIndex;
        set
        {
            if (SetProperty(ref _selectedMonitorIndex, value))
            {
                _module.Settings.MonitorTarget = (DockMonitorTarget)value;
                _module.ApplySettingsChanges();
            }
        }
    }

    public string HotkeyText
    {
        get => _hotkeyText;
        set => SetProperty(ref _hotkeyText, value);
    }

    public void AddItem(string path)
    {
        _module.AddItem(path);
    }

    public void DeleteItem(DockSettingsItemViewModel itemVm)
    {
        _module.RemoveItem(itemVm.Item);
    }

    public void MoveItem(DockSettingsItemViewModel itemVm, int direction)
    {
        var currentIndex = Items.IndexOf(itemVm);
        var targetIndex = currentIndex + direction;
        if (targetIndex >= 0 && targetIndex < Items.Count)
        {
            _module.MoveItem(currentIndex, targetIndex);
        }
    }

    private void OnModuleItemsChanged()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Items.Clear();
            foreach (var item in _module.Settings.Items)
            {
                Items.Add(new DockSettingsItemViewModel(item, this));
            }
        });
    }
}
