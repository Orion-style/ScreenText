using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;

namespace ZenithApp.Modules.Screenshot;

public sealed class ScreenshotModule : BaseFeatureModule
{
    public const string ModuleId = "screenshot";
    public const string HotkeyIdScreenshot = "screenshot_capture";
    public const string HotkeyIdOcr = "screenshot_ocr";

    private readonly ISettingsService _settingsService;
    private readonly IHotkeyService _hotkeyService;
    private readonly IEnumerable<ITextRecognizedHandler> _recognizedHandlers;

    private bool _isBusy;

    public ScreenshotModule(
        IAppLogger logger,
        ISettingsService settingsService,
        IHotkeyService hotkeyService,
        IEnumerable<ITextRecognizedHandler> recognizedHandlers) : base(logger)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _recognizedHandlers = recognizedHandlers;
    }

    public override string Id => ModuleId;
    public override string Name => "Скриншот области и OCR";
    public override string Description => "Снятие скриншота выделенной области с опциональным распознаванием текста в буфер обмена.";

    public string? HotkeyScreenshotError { get; private set; }
    public string? HotkeyOcrError { get; private set; }

    public event Action? HotkeyStatusChanged;

    protected override Task OnEnableAsync()
    {
        RegisterHotkeys();
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        UnregisterHotkeys();
        return Task.CompletedTask;
    }

    public void RegisterHotkeys()
    {
        UnregisterHotkeys();

        var config = _settingsService.Current.Screenshot;

        // Регистрация хоткея скриншота
        var scSuccess = _hotkeyService.RegisterHotkey(
            HotkeyIdScreenshot,
            config.ScreenshotModifiers,
            config.ScreenshotKey,
            () => Application.Current.Dispatcher.Invoke(() => StartCaptureSession(isOcrMode: false)));

        HotkeyScreenshotError = scSuccess 
            ? null 
            : $"Горячая клавиша {config.ScreenshotModifiers} + {config.ScreenshotKey} уже занята другим приложением.";

        // Регистрация хоткея OCR
        var ocrSuccess = _hotkeyService.RegisterHotkey(
            HotkeyIdOcr,
            config.OcrModifiers,
            config.OcrKey,
            () => Application.Current.Dispatcher.Invoke(() => StartCaptureSession(isOcrMode: true)));

        HotkeyOcrError = ocrSuccess 
            ? null 
            : $"Горячая клавиша {config.OcrModifiers} + {config.OcrKey} уже занята другим приложением.";

        HotkeyStatusChanged?.Invoke();
    }

    public void UnregisterHotkeys()
    {
        _hotkeyService.UnregisterHotkey(HotkeyIdScreenshot);
        _hotkeyService.UnregisterHotkey(HotkeyIdOcr);
        HotkeyScreenshotError = null;
        HotkeyOcrError = null;
        HotkeyStatusChanged?.Invoke();
    }

    private void StartCaptureSession(bool isOcrMode)
    {
        if (_isBusy) return;
        _isBusy = true;

        try
        {
            // 1. Быстрый снимок экрана до показа оверлея
            var virtualBounds = ScreenCaptureService.GetVirtualScreenBounds();
            var screenBmp = ScreenCaptureService.CaptureVirtualScreen();

            // 2. Создаем и показываем оверлей выделения
            var overlay = new SelectionOverlayWindow(virtualBounds, screenBmp, cropArea =>
            {
                // Завершение выделения
                Task.Run(async () =>
                {
                    try
                    {
                        if (cropArea.HasValue && cropArea.Value.Width > 0 && cropArea.Value.Height > 0)
                        {
                            using var cropped = ScreenCaptureService.CropBitmap(screenBmp, cropArea.Value);
                            if (isOcrMode)
                            {
                                await ProcessOcrAsync(cropped, cropArea.Value);
                            }
                            else
                            {
                                ProcessNormalScreenshot(cropped);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Ошибка при обработке выбранной области скриншота.", ex);
                    }
                    finally
                    {
                        screenBmp.Dispose();
                        _isBusy = false;

                        // Принудительно очищаем память после завершения сессии скриншота
                        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                        GC.WaitForPendingFinalizers();
                        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                    }
                });
            });

            overlay.Show();
        }
        catch (Exception ex)
        {
            Logger.LogError("Не удалось запустить сессию захвата экрана.", ex);
            _isBusy = false;
        }
    }

    private void ProcessNormalScreenshot(Bitmap cropped)
    {
        // 1. Копируем изображение в буфер обмена
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                var hBitmap = cropped.GetHbitmap();
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    Clipboard.SetImage(source);
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Не удалось скопировать скриншот в буфер обмена.", ex);
            }
        });

        // 2. Дополнительное сохранение в папку
        var config = _settingsService.Current.Screenshot;
        if (config.SaveToFileEnabled && !string.IsNullOrWhiteSpace(config.SaveFolderPath))
        {
            try
            {
                Directory.CreateDirectory(config.SaveFolderPath);
                var fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var fullPath = Path.Combine(config.SaveFolderPath, fileName);
                cropped.Save(fullPath, ImageFormat.Png);
                NotificationToast.Show($"Скриншот сохранён: {fileName}", "Скриншот области", ToastNotificationType.Success);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Не удалось сохранить скриншот в папку {config.SaveFolderPath}.", ex);
                NotificationToast.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", ToastNotificationType.Error);
            }
        }
        else
        {
            NotificationToast.Show("Скриншот скопирован в буфер обмена.", "Скриншот области", ToastNotificationType.Success);
        }
    }

    private async Task ProcessOcrAsync(Bitmap cropped, Rectangle cropArea)
    {
        var (text, errorMessage) = await OcrService.RecognizeTextAsync(cropped);

        if (!string.IsNullOrEmpty(errorMessage))
        {
            NotificationToast.Show(errorMessage, "Ошибка OCR", ToastNotificationType.Error, durationSeconds: 5);
            return;
        }

        // Подготавливаем аргументы события (изображение передается и сразу освобождается)
        var eventArgs = new TextRecognizedEventArgs(text ?? string.Empty, cropped, cropArea);
        bool handled = false;

        foreach (var handler in _recognizedHandlers)
        {
            try
            {
                if (await handler.OnTextRecognizedAsync(eventArgs))
                {
                    handled = true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Ошибка в обработчике ITextRecognizedHandler.", ex);
            }
        }

        // Если обработчик (например, SmartLinks) показал собственное окно или подсказку, то стандартный тост не нужен
        if (handled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Копируем текст в буфер обмена
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                Logger.LogError("Не удалось скопировать текст в буфер обмена.", ex);
            }
        });

        // Формируем превью первых слов
        var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var preview = string.Join(" ", words.Take(8));
        if (words.Length > 8)
        {
            preview += "...";
        }

        NotificationToast.Show(preview, "Текст скопирован", ToastNotificationType.Success);
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
