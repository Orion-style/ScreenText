using System.Windows.Input;
using ZenithApp.Core.Settings;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.DynamicIsland;

public sealed class DynamicIslandSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly DynamicIslandModule _module;
    private readonly ISettingsService _settingsService;

    private bool _showAppNotifications;
    private bool _showNextSessionCountdown;
    private bool _showMediaControls;
    private bool _autoHideWhenIdle;
    private bool _hideInFullscreen;
    private bool _removeShelfItemAfterDrag;
    private int _topMarginPx;
    private string _shelfSummaryText = string.Empty;
    private bool _hasShelfItems;
    private bool _disposed;

    public DynamicIslandSettingsViewModel(DynamicIslandModule module, ISettingsService settingsService)
    {
        _module = module;
        _settingsService = settingsService;

        var s = _settingsService.Current.DynamicIsland;
        _showAppNotifications = s.ShowAppNotifications;
        _showNextSessionCountdown = s.ShowNextSessionCountdown;
        _showMediaControls = s.ShowMediaControls;
        _autoHideWhenIdle = s.AutoHideWhenIdle;
        _hideInFullscreen = s.HideInFullscreen;
        _removeShelfItemAfterDrag = s.RemoveShelfItemAfterDrag;
        _topMarginPx = Math.Clamp(s.TopMarginPx, 0, 32);

        ExpandIslandCommand = new RelayCommand(() => _module.ExpandIsland());
        AddClipboardCommand = new RelayCommand(() => _module.AddClipboardToShelf());
        ClearShelfCommand = new RelayCommand(() => _module.Shelf.Clear());

        _module.Shelf.ItemsChanged += OnShelfChanged;
        RefreshShelfInfo();
    }

    public ICommand ExpandIslandCommand { get; }
    public ICommand AddClipboardCommand { get; }
    public ICommand ClearShelfCommand { get; }

    public bool ShowAppNotifications
    {
        get => _showAppNotifications;
        set
        {
            if (SetProperty(ref _showAppNotifications, value))
            {
                _settingsService.Current.DynamicIsland.ShowAppNotifications = value;
                SaveAndApply();
            }
        }
    }

    public bool ShowNextSessionCountdown
    {
        get => _showNextSessionCountdown;
        set
        {
            if (SetProperty(ref _showNextSessionCountdown, value))
            {
                _settingsService.Current.DynamicIsland.ShowNextSessionCountdown = value;
                SaveAndApply();
            }
        }
    }

    public bool ShowMediaControls
    {
        get => _showMediaControls;
        set
        {
            if (SetProperty(ref _showMediaControls, value))
            {
                _settingsService.Current.DynamicIsland.ShowMediaControls = value;
                SaveAndApply();
            }
        }
    }

    public bool AutoHideWhenIdle
    {
        get => _autoHideWhenIdle;
        set
        {
            if (SetProperty(ref _autoHideWhenIdle, value))
            {
                _settingsService.Current.DynamicIsland.AutoHideWhenIdle = value;
                SaveAndApply();
            }
        }
    }

    public bool HideInFullscreen
    {
        get => _hideInFullscreen;
        set
        {
            if (SetProperty(ref _hideInFullscreen, value))
            {
                _settingsService.Current.DynamicIsland.HideInFullscreen = value;
                SaveAndApply();
            }
        }
    }

    public bool RemoveShelfItemAfterDrag
    {
        get => _removeShelfItemAfterDrag;
        set
        {
            if (SetProperty(ref _removeShelfItemAfterDrag, value))
            {
                _settingsService.Current.DynamicIsland.RemoveShelfItemAfterDrag = value;
                SaveAndApply();
            }
        }
    }

    public int TopMarginPx
    {
        get => _topMarginPx;
        set
        {
            var clamped = Math.Clamp(value, 0, 32);
            if (SetProperty(ref _topMarginPx, clamped))
            {
                _settingsService.Current.DynamicIsland.TopMarginPx = clamped;
                SaveAndApply();
            }
        }
    }

    public string ShelfSummaryText
    {
        get => _shelfSummaryText;
        private set => SetProperty(ref _shelfSummaryText, value);
    }

    public bool HasShelfItems
    {
        get => _hasShelfItems;
        private set => SetProperty(ref _hasShelfItems, value);
    }

    public void RefreshShelfInfo()
    {
        int count = _module.Shelf.Count;
        HasShelfItems = count > 0;
        ShelfSummaryText = count > 0
            ? $"На полке сейчас элементов: {count}. Кликните на островок сверху экрана, чтобы перетащить их."
            : "Полка пуста. Перетащите текст, ссылки или файлы прямо на островок сверху экрана.";
    }

    private void OnShelfChanged()
    {
        RefreshShelfInfo();
    }

    private void SaveAndApply()
    {
        _settingsService.Save();
        _module.NotifySettingsUpdated();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _module.Shelf.ItemsChanged -= OnShelfChanged;
    }
}
