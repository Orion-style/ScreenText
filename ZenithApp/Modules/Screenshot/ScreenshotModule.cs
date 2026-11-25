using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
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
    private readonly IOcrHistoryService _historyService;

    private bool _isBusy;
    private Rectangle? _savedCropArea;

    // Делегат для подмены проверки нажатия клавиши Alt в автоматических тестах
    public static Func<bool>? AltKeyChecker { get; set; }

    public ScreenshotModule(
        IAppLogger logger,
        ISettingsService settingsService,
        IHotkeyService hotkeyService,
        IEnumerable<ITextRecognizedHandler> recognizedHandlers,
        IOcrHistoryService historyService) : base(logger)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _recognizedHandlers = recognizedHandlers;
        _historyService = historyService;
    }

    public override string Id => ModuleId;
    public override string Name => "Скриншот области и OCR";
    public override string Description => "Снятие скриншота выделенной области с опциональным распознаванием текста в буфер обмена.";

    public string? HotkeyScreenshotError { get; private set; }
    public string? HotkeyOcrError { get; private set; }

    public event Action? HotkeyStatusChanged;
    public event Action? SavedAreaChanged;

    public Rectangle? SavedCropArea => _savedCropArea;
    public bool HasSavedArea => _savedCropArea.HasValue;

    public void ResetSavedArea(bool notifyUser = true)
    {
        if (_savedCropArea.HasValue)
        {
            _savedCropArea = null;
            SavedAreaChanged?.Invoke();
            if (notifyUser)
            {
                NotificationToast.Show("Сохранённая область сброшена. При следующем вызове откроется выбор области.", "Сброс области", ToastNotificationType.Info);
            }
        }
    }

    public void SetSavedArea(Rectangle area)
    {
        _savedCropArea = area;
        SavedAreaChanged?.Invoke();
    }

    protected override Task OnEnableAsync()
    {
        RegisterHotkeys();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        UnregisterHotkeys();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _savedCropArea = null;
        SavedAreaChanged?.Invoke();
        return Task.CompletedTask;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_savedCropArea.HasValue)
        {
            var bounds = ScreenCaptureService.GetVirtualScreenBounds();
            var screenRect = new Rectangle(0, 0, bounds.Width, bounds.Height);
            if (!_savedCropArea.Value.IntersectsWith(screenRect))
            {
                _savedCropArea = null;
                SavedAreaChanged?.Invoke();
            }
        }
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
            () => Application.Current.Dispatcher.Invoke(() => HandleHotkeyTriggered(isOcrMode: false)));

        HotkeyScreenshotError = scSuccess 
            ? null 
            : $"Горячая клавиша {config.ScreenshotModifiers} + {config.ScreenshotKey} уже занята другим приложением.";

        // Регистрация хоткея OCR
        var ocrSuccess = _hotkeyService.RegisterHotkey(
            HotkeyIdOcr,
            config.OcrModifiers,
            config.OcrKey,
            () => Application.Current.Dispatcher.Invoke(() => HandleHotkeyTriggered(isOcrMode: true)));

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

    private void HandleHotkeyTriggered(bool isOcrMode)
    {
        // Если при нажатии хоткея удерживается клавиша Alt, принудительно сбрасываем область и выбираем новую
        bool isAltPressed = AltKeyChecker?.Invoke() ?? IsPhysicalAltPressed();
        if (isAltPressed && _savedCropArea.HasValue)
        {
            _savedCropArea = null;
            SavedAreaChanged?.Invoke();
        }

        StartCaptureSession(isOcrMode);
    }

    public void StartCaptureSession(bool isOcrMode)
    {
        if (_isBusy) return;
        _isBusy = true;

        try
        {
            var config = _settingsService.Current.Screenshot;

            // ДВУХШАГОВЫЙ РЕЖИМ: Второй/последующий вызов (мгновенный снимок зафиксированной области)
            if (config.TwoStepCaptureEnabled && _savedCropArea.HasValue)
            {
                var cropArea = _savedCropArea.Value;

                Task.Run(async () =>
                {
                    try
                    {
                        var virtualBounds = ScreenCaptureService.GetVirtualScreenBounds();
                        cropArea.Intersect(new Rectangle(0, 0, virtualBounds.Width, virtualBounds.Height));

                        if (cropArea.Width <= 3 || cropArea.Height <= 3)
                        {
                            // Область невалидна (например, разрешение изменилось) - сбрасываем и открываем оверлей
                            _savedCropArea = null;
                            SavedAreaChanged?.Invoke();
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                _isBusy = false;
                                StartCaptureSession(isOcrMode);
                            });
                            return;
                        }

                        using var screenBmp = ScreenCaptureService.CaptureVirtualScreen();
                        using var cropped = ScreenCaptureService.CropBitmap(screenBmp, cropArea);

                        if (isOcrMode)
                        {
                            await ProcessOcrAsync(cropped, cropArea, isRepeatCapture: true);
                        }
                        else
                        {
                            ProcessNormalScreenshot(cropped, isRepeatCapture: true);
                        }

                        // Если включен циклический 2-шаговый режим: сбрасываем область после 2-го шага
                        if (config.TwoStepResetAfterCapture)
                        {
                            _savedCropArea = null;
                            SavedAreaChanged?.Invoke();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Ошибка при мгновенном снимке сохранённой области.", ex);
                    }
                    finally
                    {
                        _isBusy = false;
                        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                        GC.WaitForPendingFinalizers();
                        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                    }
                });
                return;
            }

            // ПЕРВЫЙ ВЫЗОВ (выбор области через полноэкранный оверлей)
            var currentVirtualBounds = ScreenCaptureService.GetVirtualScreenBounds();
            var screenCaptureBmp = ScreenCaptureService.CaptureVirtualScreen();

            var overlay = new SelectionOverlayWindow(currentVirtualBounds, screenCaptureBmp, cropArea =>
            {
                Task.Run(async () =>
                {
                    try
                    {
                        if (cropArea.HasValue && cropArea.Value.Width > 3 && cropArea.Value.Height > 3)
                        {
                            if (config.TwoStepCaptureEnabled)
                            {
                                _savedCropArea = cropArea.Value;
                                SavedAreaChanged?.Invoke();
                            }

                            bool shouldCaptureNow = !config.TwoStepCaptureEnabled || config.CaptureOnFirstCall;

                            if (shouldCaptureNow)
                            {
                                using var cropped = ScreenCaptureService.CropBitmap(screenCaptureBmp, cropArea.Value);
                                if (isOcrMode)
                                {
                                    await ProcessOcrAsync(cropped, cropArea.Value, isRepeatCapture: false);
                                }
                                else
                                {
                                    ProcessNormalScreenshot(cropped, isRepeatCapture: false);
                                }
                            }
                            else
                            {
                                NotificationToast.Show(
                                    $"Область выбрана: {cropArea.Value.Width}×{cropArea.Value.Height}. Нажмите хоткей для создания снимка этой области.",
                                    "Область зафиксирована",
                                    ToastNotificationType.Info);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Ошибка при обработке выбранной области скриншота.", ex);
                    }
                    finally
                    {
                        screenCaptureBmp.Dispose();
                        _isBusy = false;

                        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                        GC.WaitForPendingFinalizers();
                        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                    }
                });
            }, _savedCropArea, config);

            overlay.Show();
        }
        catch (Exception ex)
        {
            Logger.LogError("Не удалось запустить сессию захвата экрана.", ex);
            _isBusy = false;
        }
    }

    private void ProcessNormalScreenshot(Bitmap cropped, bool isRepeatCapture)
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
        string title = isRepeatCapture ? "Мгновенный снимок области" : "Скриншот области";
        string hint = (config.TwoStepCaptureEnabled && !isRepeatCapture)
            ? " (Область сохранена: следующий вызов снимет её мгновенно)"
            : "";

        if (config.SaveToFileEnabled && !string.IsNullOrWhiteSpace(config.SaveFolderPath))
        {
            try
            {
                Directory.CreateDirectory(config.SaveFolderPath);
                var fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var fullPath = Path.Combine(config.SaveFolderPath, fileName);
                cropped.Save(fullPath, ImageFormat.Png);
                NotificationToast.Show($"Скриншот сохранён: {fileName}{hint}", title, ToastNotificationType.Success);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Не удалось сохранить скриншот в папку {config.SaveFolderPath}.", ex);
                NotificationToast.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", ToastNotificationType.Error);
            }
        }
        else
        {
            NotificationToast.Show($"Скриншот скопирован в буфер обмена.{hint}", title, ToastNotificationType.Success);
        }
    }

    private async Task ProcessOcrAsync(Bitmap cropped, Rectangle cropArea, bool isRepeatCapture)
    {
        var (text, errorMessage) = await OcrService.RecognizeTextAsync(cropped);

        if (!string.IsNullOrEmpty(errorMessage))
        {
            NotificationToast.Show(errorMessage, "Ошибка OCR", ToastNotificationType.Error, durationSeconds: 5);
            return;
        }

        // Подготавливаем аргументы события (изображение передается и сразу освобождается)
        var eventArgs = new TextRecognizedEventArgs(text ?? string.Empty, cropped, cropArea);
        if (!string.IsNullOrWhiteSpace(text))
        {
            _historyService.Add(text);
        }
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

        string title = isRepeatCapture ? "Текст скопирован (повторный снимок)" : "Текст скопирован";
        NotificationToast.Show(preview, title, ToastNotificationType.Success);
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    private const int VK_MENU = 0x12;

    private static bool IsPhysicalAltPressed()
    {
        try
        {
            return (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
