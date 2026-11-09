using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Wpf.Ui.Tray.Controls;
using ZenithApp.Core.Logging;
using ZenithApp.Modules.Screenshot;

namespace ZenithApp.UI.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly IAppLogger _logger;
    private readonly Action _openSettingsAction;
    private readonly Action _exitAction;
    private readonly IClipboardOcrService _clipboardOcrService;
    private readonly IOcrHistoryService _historyService;

    private NotifyIcon? _notifyIcon;
    private MenuItem? _historySubMenu;
    private bool _disposed;

    public TrayIconService(
        IAppLogger logger,
        Action openSettingsAction,
        Action exitAction,
        IClipboardOcrService clipboardOcrService,
        IOcrHistoryService historyService)
    {
        _logger = logger;
        _openSettingsAction = openSettingsAction;
        _exitAction = exitAction;
        _clipboardOcrService = clipboardOcrService;
        _historyService = historyService;
    }

    public void Initialize()
    {
        if (_notifyIcon != null) return;

        try
        {
            var contextMenu = new ContextMenu();

            // 1. Быстрое распознавание текста из буфера
            var ocrClipboardItem = new MenuItem
            {
                Header = "Распознать текст из буфера"
            };
            ocrClipboardItem.Click += async (_, _) =>
            {
                try
                {
                    await _clipboardOcrService.RecognizeTextFromClipboardAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError("Ошибка распознавания из буфера обмена.", ex);
                }
            };

            // 2. Декодирование QR-кода из буфера
            var qrClipboardItem = new MenuItem
            {
                Header = "Декодировать QR-код из буфера"
            };
            qrClipboardItem.Click += async (_, _) =>
            {
                try
                {
                    await _clipboardOcrService.DecodeQrFromClipboardAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError("Ошибка декодирования QR из буфера обмена.", ex);
                }
            };

            // 3. Подменю истории распознанного текста
            _historySubMenu = new MenuItem { Header = "История распознаваний" };
            BuildHistorySubMenu(_historySubMenu);
            _historyService.HistoryChanged += OnHistoryChanged;

            // 4. Настройки и Выход
            var openSettingsItem = new MenuItem { Header = "Настройки" };
            openSettingsItem.Click += (_, _) => _openSettingsAction.Invoke();

            var exitItem = new MenuItem { Header = "Выход" };
            exitItem.Click += (_, _) => _exitAction.Invoke();

            contextMenu.Items.Add(ocrClipboardItem);
            contextMenu.Items.Add(qrClipboardItem);
            contextMenu.Items.Add(_historySubMenu);
            contextMenu.Items.Add(new Separator());
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

            _logger.LogInfo("WPF-UI Tray icon initialized with OCR and history actions.");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to initialize WPF-UI Tray icon.", ex);
        }
    }

    private void OnHistoryChanged()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_historySubMenu != null)
            {
                BuildHistorySubMenu(_historySubMenu);
            }
        });
    }

    private void BuildHistorySubMenu(MenuItem menu)
    {
        menu.Items.Clear();
        var history = _historyService.GetHistory();

        if (history.Count == 0)
        {
            var emptyItem = new MenuItem
            {
                Header = "(История пуста)",
                IsEnabled = false
            };
            menu.Items.Add(emptyItem);
            return;
        }

        foreach (var item in history)
        {
            var text = item;
            var singleLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
            var preview = singleLine.Length > 40 ? singleLine[..40] + "..." : singleLine;

            var menuItem = new MenuItem
            {
                Header = preview,
                ToolTip = text
            };
            menuItem.Click += (_, _) =>
            {
                try
                {
                    Clipboard.SetText(text);
                    NotificationToast.Show("Текст скопирован в буфер обмена.", "История OCR", ToastNotificationType.Success);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Ошибка копирования из истории.", ex);
                }
            };
            menu.Items.Add(menuItem);
        }

        menu.Items.Add(new Separator());
        var clearItem = new MenuItem { Header = "Очистить историю" };
        clearItem.Click += (_, _) => _historyService.Clear();
        menu.Items.Add(clearItem);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _historyService.HistoryChanged -= OnHistoryChanged;

        if (_notifyIcon != null)
        {
            _notifyIcon.Unregister();
            _notifyIcon = null;
        }
    }
}
