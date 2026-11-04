using System.Windows;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;
using ZenithApp.Modules.Screenshot;

namespace ZenithApp.Modules.SmartLinks;

public sealed class SmartLinksModule : BaseFeatureModule, ITextRecognizedHandler
{
    public const string ModuleId = "smart_links";

    private readonly ISettingsService _settingsService;
    private SmartLinksPopupWindow? _currentPopup;

    public SmartLinksModule(IAppLogger logger, ISettingsService settingsService) : base(logger)
    {
        _settingsService = settingsService;
    }

    public override string Id => ModuleId;
    public override string Name => "Умные подсказки для ссылок и QR";
    public override string Description => "Автоматическое открытие ссылок, декодирование QR-кодов и умные действия (поиск, перевод, переход) для распознанного текста.";

    protected override Task OnEnableAsync()
    {
        Logger.LogInfo("[SmartLinksModule] Включен. Обработчик ITextRecognizedHandler активен.");
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        Logger.LogInfo("[SmartLinksModule] Отключен. Закрытие открытых окон.");
        Application.Current?.Dispatcher.Invoke(() =>
        {
            try
            {
                _currentPopup?.Close();
                _currentPopup = null;
            }
            catch { }
        });
        return Task.CompletedTask;
    }

    public async Task<bool> OnTextRecognizedAsync(TextRecognizedEventArgs args)
    {
        if (!IsEnabled)
        {
            return false;
        }

        try
        {
            var config = _settingsService.Current.SmartLinks;

            // 1. Попытка декодировать QR-код на исходном фрагменте изображения
            string? qrText = null;
            if (args.Image != null)
            {
                qrText = QrCodeService.TryDecodeQrCode(args.Image);
            }

            bool isFromQr = !string.IsNullOrWhiteSpace(qrText);
            string content = isFromQr ? qrText! : args.Text;

            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            // Копируем итоговый контент в буфер обмена (если QR найден, он заменяет распознанный текст)
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    Clipboard.SetText(content);
                }
                catch (Exception ex)
                {
                    Logger.LogError("[SmartLinksModule] Ошибка записи в буфер обмена.", ex);
                }
            });

            // 2. Классификация содержимого
            var classification = ContentClassifier.Classify(content, isFromQr);

            // 3. Автоматическое открытие одиночной ссылки
            if (classification.Kind == ContentKind.SingleUrl)
            {
                string singleUrl = classification.Urls[0];
                bool shouldAutoOpen = (!isFromQr && config.AutoOpenUrls) || (isFromQr && config.AutoOpenQrUrls);

                if (shouldAutoOpen)
                {
                    bool opened = ContentClassifier.SafeOpenInBrowser(singleUrl);
                    if (opened)
                    {
                        var domainMsg = ContentClassifier.FormatDomainForToast(singleUrl);
                        NotificationToast.Show(domainMsg, "Открыта ссылка", ToastNotificationType.Success, durationSeconds: 2);
                        return true; // Ссылка открыта автоматически, показывать всплывающую подсказку не нужно
                    }
                }
            }

            // 4. Если это обычный текст и в настройках отключен показ подсказки для обычного текста:
            if (classification.Kind == ContentKind.PlainText && !config.ShowPopupForPlainText)
            {
                // Не показываем подсказку, но текст скопирован, поэтому пусть ScreenshotModule покажет тост
                return false;
            }

            // 5. Показ всплывающей подсказки
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _currentPopup?.Close();
                    _currentPopup = new SmartLinksPopupWindow(classification, config, args.SelectionBoundsScreen);
                    _currentPopup.Closed += (_, _) => _currentPopup = null;
                    _currentPopup.Show();
                }
                catch (Exception ex)
                {
                    Logger.LogError("[SmartLinksModule] Ошибка отображения всплывающей подсказки.", ex);
                }
            });

            return true; // Подсказка отображена, стандартный тост «Текст скопирован» заменяется ею
        }
        catch (Exception ex)
        {
            Logger.LogError("[SmartLinksModule] Сбой в обработке содержимого.", ex);
            return false;
        }
    }
}
