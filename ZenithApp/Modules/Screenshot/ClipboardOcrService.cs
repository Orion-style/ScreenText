using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ZenithApp.Modules.SmartLinks;

namespace ZenithApp.Modules.Screenshot;

public interface IClipboardOcrService
{
    Task RecognizeTextFromClipboardAsync();
    Task DecodeQrFromClipboardAsync();
}

public sealed class ClipboardOcrService : IClipboardOcrService
{
    private readonly IOcrHistoryService _historyService;

    public ClipboardOcrService(IOcrHistoryService historyService)
    {
        _historyService = historyService;
    }

    public async Task RecognizeTextFromClipboardAsync()
    {
        using var bitmap = GetBitmapFromClipboard();
        if (bitmap == null)
        {
            NotificationToast.Show(
                "В буфере обмена не найдено изображение или файл картинки.",
                "Распознавание из буфера",
                ToastNotificationType.Info);
            return;
        }

        var (text, error) = await OcrService.RecognizeTextAsync(bitmap);
        if (!string.IsNullOrWhiteSpace(text))
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Clipboard.SetText(text);
            });

            _historyService.Add(text);

            var preview = text.Length > 90 ? text[..90] + "..." : text;
            NotificationToast.Show(preview, "Текст из буфера скопирован", ToastNotificationType.Success);
        }
        else
        {
            NotificationToast.Show(
                error ?? "Текст на изображении из буфера обмена не обнаружен.",
                "Распознавание текста",
                ToastNotificationType.Info);
        }
    }

    public Task DecodeQrFromClipboardAsync()
    {
        using var bitmap = GetBitmapFromClipboard();
        if (bitmap == null)
        {
            NotificationToast.Show(
                "В буфере обмена не найдено изображение или файл картинки.",
                "Декодирование QR",
                ToastNotificationType.Info);
            return Task.CompletedTask;
        }

        var qrText = QrCodeService.TryDecodeQrCode(bitmap);
        if (!string.IsNullOrWhiteSpace(qrText))
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Clipboard.SetText(qrText);
            });

            _historyService.Add(qrText);

            NotificationToast.Show(qrText, "QR-код распознан и скопирован", ToastNotificationType.Success);

            if (ContentClassifier.IsValidHttpUrl(qrText, out var validUrl))
            {
                ContentClassifier.SafeOpenInBrowser(validUrl);
            }
        }
        else
        {
            NotificationToast.Show(
                "QR-код или штрихкод на изображении не обнаружен.",
                "Декодирование QR",
                ToastNotificationType.Info);
        }

        return Task.CompletedTask;
    }

    public static System.Drawing.Bitmap? GetBitmapFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsImage())
            {
                var imageSource = Clipboard.GetImage();
                if (imageSource != null)
                {
                    using var ms = new MemoryStream();
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(imageSource));
                    encoder.Save(ms);
                    ms.Seek(0, SeekOrigin.Begin);
                    return new System.Drawing.Bitmap(ms);
                }
            }

            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                if (files.Count > 0)
                {
                    var filePath = files[0];
                    if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                    {
                        var ext = Path.GetExtension(filePath).ToLowerInvariant();
                        if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".gif" or ".tiff")
                        {
                            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            return new System.Drawing.Bitmap(fs);
                        }
                    }
                }
            }
        }
        catch
        {
            // Исключения доступа к занятому буферу обмена
        }

        return null;
    }
}
