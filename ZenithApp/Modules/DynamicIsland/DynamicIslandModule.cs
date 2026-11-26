using System.IO;
using System.Windows;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;
using ZenithApp.Modules.NextSession;
using ZenithApp.Modules.Screenshot;

namespace ZenithApp.Modules.DynamicIsland;

public interface IDynamicIslandHost
{
    void ShowNotification(string message, string? title, ToastNotificationType type, int durationSeconds);
    Task<NextSessionCountdownOutcome> WaitWithCountdownAsync(
        int delaySeconds,
        IReadOnlyList<string> programNames,
        string? messagePreview,
        CancellationToken cancellationToken);
    void UpdateMediaState(IslandMediaState state);
    void RefreshShelf();
    void ApplySettings(DynamicIslandSettings settings);
    void ExpandDrawer();
    void CloseHost();
}

public sealed class DynamicIslandModule : BaseFeatureModule, INextSessionCountdownPresenter
{
    public const string ModuleId = "dynamic_island";

    private readonly ISettingsService _settingsService;
    private readonly DynamicIslandShelfService _shelfService;
    private readonly IslandMediaService _mediaService;
    private readonly Func<IDynamicIslandHost?>? _customHostFactory;

    private IDynamicIslandHost? _host;

    public DynamicIslandModule(
        IAppLogger logger,
        ISettingsService settingsService,
        DynamicIslandShelfService? shelfService = null,
        IslandMediaService? mediaService = null,
        Func<IDynamicIslandHost?>? customHostFactory = null) : base(logger)
    {
        _settingsService = settingsService;
        _shelfService = shelfService ?? new DynamicIslandShelfService(
            logger,
            Path.Combine(_settingsService.SettingsDirectory, "island_shelf.json"));
        _mediaService = mediaService ?? new IslandMediaService(logger);
        _customHostFactory = customHostFactory;
    }

    public override string Id => ModuleId;
    public override string Name => "Островок (Dynamic Island)";
    public override string Description => "Анимированный островок сверху экрана: уведомления приложения, таймер следующего сеанса, плеер и полка буфера обмена.";

    public DynamicIslandSettings Settings => _settingsService.Current.DynamicIsland;
    public DynamicIslandShelfService Shelf => _shelfService;
    public IslandMediaService Media => _mediaService;

    bool INextSessionCountdownPresenter.IsActive =>
        IsEnabled && Settings.ShowNextSessionCountdown && _host != null;

    protected override async Task OnEnableAsync()
    {
        _shelfService.PruneMissingFiles();

        _host = CreateHost();
        if (_host != null)
        {
            _host.ApplySettings(Settings);
            _host.RefreshShelf();
        }

        _shelfService.ItemsChanged += OnShelfItemsChanged;
        _mediaService.StateChanged += OnMediaStateChanged;

        if (Settings.ShowMediaControls)
        {
            await _mediaService.StartAsync();
        }

        NotificationToast.SetInterceptor(TryInterceptNotification);
    }

    protected override Task OnDisableAsync()
    {
        NotificationToast.SetInterceptor(null);

        _shelfService.ItemsChanged -= OnShelfItemsChanged;
        _mediaService.StateChanged -= OnMediaStateChanged;
        _mediaService.Stop();

        _host?.CloseHost();
        _host = null;

        return Task.CompletedTask;
    }

    public bool TryInterceptNotification(string message, string? title, ToastNotificationType type, int durationSeconds)
    {
        if (!IsEnabled || !Settings.ShowAppNotifications || _host == null)
        {
            return false;
        }

        _host.ShowNotification(message, title, type, durationSeconds);
        return true;
    }

    public Task<NextSessionCountdownOutcome> WaitWithCountdownAsync(
        int delaySeconds,
        IReadOnlyList<string> programNames,
        string? messagePreview,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled || !Settings.ShowNextSessionCountdown || _host == null)
        {
            return Task.FromResult(NextSessionCountdownOutcome.Completed);
        }

        return _host.WaitWithCountdownAsync(delaySeconds, programNames, messagePreview, cancellationToken);
    }

    public void NotifySettingsUpdated()
    {
        if (!IsEnabled) return;

        if (Settings.ShowMediaControls)
        {
            _ = _mediaService.StartAsync();
        }
        else
        {
            _mediaService.Stop();
        }

        _host?.ApplySettings(Settings);
    }

    public void ExpandIsland()
    {
        if (!IsEnabled) return;
        _host?.ExpandDrawer();
    }

    public bool AddClipboardToShelf()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return false;

        bool added = false;
        void AddCore()
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var list = Clipboard.GetFileDropList();
                    var files = new List<string>(list.Count);
                    foreach (string? item in list)
                    {
                        if (!string.IsNullOrWhiteSpace(item))
                        {
                            files.Add(item);
                        }
                    }
                    added = _shelfService.AddFiles(files) > 0;
                    return;
                }

                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    added = _shelfService.AddText(text);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[DynamicIsland] Failed to read clipboard: {ex.Message}");
            }
        }

        if (dispatcher.CheckAccess())
        {
            AddCore();
        }
        else
        {
            dispatcher.Invoke(AddCore);
        }

        return added;
    }

    private void OnShelfItemsChanged()
    {
        _host?.RefreshShelf();
    }

    private void OnMediaStateChanged(IslandMediaState state)
    {
        _host?.UpdateMediaState(state);
    }

    private IDynamicIslandHost? CreateHost()
    {
        if (_customHostFactory != null)
        {
            return _customHostFactory();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return null;
        }

        DynamicIslandWindow? window = null;
        void CreateAndShow()
        {
            window = new DynamicIslandWindow(this, _shelfService, _mediaService, Logger);
            window.Show();
        }

        if (dispatcher.CheckAccess())
        {
            CreateAndShow();
        }
        else
        {
            dispatcher.Invoke(CreateAndShow);
        }

        return window;
    }

    public override void Dispose()
    {
        base.Dispose();
        _mediaService.Dispose();
    }
}
