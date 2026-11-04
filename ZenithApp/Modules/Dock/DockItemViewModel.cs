using System.Windows.Input;
using System.Windows.Media;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.Dock;

public sealed class DockItemViewModel : ViewModelBase
{
    private readonly DockItem _item;
    private ImageSource? _icon;
    private bool _isRunning;
    private bool _isMissing;
    private double _scale = 1.0;
    private double _bounceOffsetY = 0.0;

    public DockItem Item => _item;

    public DockItemViewModel(DockItem item)
    {
        _item = item;
        ReloadIcon();
    }

    public string Id => _item.Id;

    public string Title
    {
        get => string.IsNullOrWhiteSpace(_item.Title) ? System.IO.Path.GetFileNameWithoutExtension(_item.TargetPath) : _item.Title;
        set
        {
            if (_item.Title != value)
            {
                _item.Title = value;
                OnPropertyChanged();
            }
        }
    }

    public string TargetPath
    {
        get => _item.TargetPath;
        set
        {
            if (_item.TargetPath != value)
            {
                _item.TargetPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Title));
                ReloadIcon();
            }
        }
    }

    public ImageSource? Icon
    {
        get => _icon;
        private set => SetProperty(ref _icon, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    public bool IsMissing
    {
        get => _isMissing;
        set => SetProperty(ref _isMissing, value);
    }

    public double Scale
    {
        get => _scale;
        set => SetProperty(ref _scale, value);
    }

    public double BounceOffsetY
    {
        get => _bounceOffsetY;
        set => SetProperty(ref _bounceOffsetY, value);
    }

    public void ReloadIcon()
    {
        var iconSource = IconExtractionService.GetIcon(_item.TargetPath, 64);
        if (iconSource == null)
        {
            IsMissing = true;
        }
        else
        {
            IsMissing = false;
            Icon = iconSource;
        }
    }

    public void UpdateRunningState()
    {
        IsRunning = ProcessInteractionService.IsRunning(_item.TargetPath, out _);
    }
}
