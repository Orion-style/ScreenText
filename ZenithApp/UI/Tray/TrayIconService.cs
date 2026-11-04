using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Wpf.Ui.Tray.Controls;
using ZenithApp.Core.Logging;

namespace ZenithApp.UI.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly IAppLogger _logger;
    private readonly Action _openSettingsAction;
    private readonly Action _exitAction;
    private NotifyIcon? _notifyIcon;
    private bool _disposed;

    public TrayIconService(
        IAppLogger logger,
        Action openSettingsAction,
        Action exitAction)
    {
        _logger = logger;
        _openSettingsAction = openSettingsAction;
        _exitAction = exitAction;
    }

    public void Initialize()
    {
        if (_notifyIcon != null) return;

        try
        {
            var contextMenu = new ContextMenu();

            var openSettingsItem = new MenuItem { Header = "Настройки" };
            openSettingsItem.Click += (_, _) => _openSettingsAction.Invoke();

            var exitItem = new MenuItem { Header = "Выход" };
            exitItem.Click += (_, _) => _exitAction.Invoke();

            contextMenu.Items.Add(openSettingsItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitItem);

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "app.ico");
            var iconSource = File.Exists(iconPath)
                ? new BitmapImage(new Uri(iconPath, UriKind.Absolute))
                : null;

            _notifyIcon = new NotifyIcon
            {
                TooltipText = "Zenith Utility",
                Menu = contextMenu,
                MenuOnRightClick = true,
                Icon = iconSource!
            };

#pragma warning disable CS8622
            _notifyIcon.LeftDoubleClick += (s, e) => _openSettingsAction.Invoke();
#pragma warning restore CS8622

            _notifyIcon.Register();

            _logger.LogInfo("WPF-UI Tray icon initialized and registered successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to initialize WPF-UI Tray icon.", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_notifyIcon != null)
        {
            _notifyIcon.Unregister();
            _notifyIcon = null;
        }
    }
}
