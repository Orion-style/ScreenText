using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ZenithApp.Modules.Screenshot;

public static class OcrService
{
    private static readonly object _enginesLock = new();
    private static List<OcrEngine>? _cachedEngines;
    private static Timer? _idleUnloadTimer;
    private static readonly TimeSpan IdleUnloadTimeout = TimeSpan.FromMinutes(3);

    public static (bool HasRussian, bool HasEnglish) CheckAvailableLanguages()
    {
        var langs = OcrEngine.AvailableRecognizerLanguages;
        bool hasRu = langs.Any(l => l.LanguageTag.StartsWith("ru", StringComparison.OrdinalIgnoreCase));
        bool hasEn = langs.Any(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        return (hasRu, hasEn);
    }

    public static async Task<(string Text, string? ErrorMessage)> RecognizeTextAsync(Bitmap bitmap)
    {
        var (hasRu, hasEn) = CheckAvailableLanguages();
        if (!hasRu && !hasEn && !OcrEngine.AvailableRecognizerLanguages.Any())
        {
            return (string.Empty, "В системе Windows не установлены языковые пакеты OCR (русский или английский).\n" +
                                  "Чтобы добавить: откройте «Параметры Windows» -> «Время и язык» -> «Язык» -> выберите язык и установите компонент «Оптическое распознавание символов».");
        }

        var engines = GetOrCreateCachedEngines();
        if (engines.Count == 0)
        {
            return (string.Empty, "Не найден подходящий модуль распознавания Windows.Media.Ocr.");
        }

        try
        {
            // 1. Пробуем распознать исходное изображение
            var text = await RecognizeBestResultAsync(bitmap, engines);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return (text, null);
            }

            // 2. Если текста нет, увеличиваем мелкий фрагмент с интерполяцией
            if (bitmap.Width < 1400 && bitmap.Height < 1400)
            {
                using var upscaled = UpscaleBitmap(bitmap, 2.5f);
                var upscaledText = await RecognizeBestResultAsync(upscaled, engines);
                if (!string.IsNullOrWhiteSpace(upscaledText))
                {
                    return (upscaledText, null);
                }
            }

            return (string.Empty, null);
        }
        catch (Exception ex)
        {
            return (string.Empty, $"Ошибка распознавания: {ex.Message}");
        }
    }

    private static List<OcrEngine> GetOrCreateCachedEngines()
    {
        lock (_enginesLock)
        {
            // Перезапускаем таймер освобождения памяти при каждом распознавании
            _idleUnloadTimer?.Dispose();
            _idleUnloadTimer = new Timer(_ => UnloadEngines(), null, IdleUnloadTimeout, Timeout.InfiniteTimeSpan);

            if (_cachedEngines != null && _cachedEngines.Count > 0)
            {
                return _cachedEngines;
            }

            var engines = new List<OcrEngine>();

            var ruLang = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(l => l.LanguageTag.StartsWith("ru", StringComparison.OrdinalIgnoreCase));
            if (ruLang != null)
            {
                var ruEngine = OcrEngine.TryCreateFromLanguage(ruLang);
                if (ruEngine != null) engines.Add(ruEngine);
            }

            var enLang = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            if (enLang != null)
            {
                var enEngine = OcrEngine.TryCreateFromLanguage(enLang);
                if (enEngine != null) engines.Add(enEngine);
            }

            var userEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (userEngine != null && engines.All(e => !e.RecognizerLanguage.LanguageTag.Equals(userEngine.RecognizerLanguage.LanguageTag, StringComparison.OrdinalIgnoreCase)))
            {
                engines.Add(userEngine);
            }

            if (engines.Count == 0)
            {
                var fallback = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                if (fallback != null)
                {
                    var fbEngine = OcrEngine.TryCreateFromLanguage(fallback);
                    if (fbEngine != null) engines.Add(fbEngine);
                }
            }

            _cachedEngines = engines;
            return _cachedEngines;
        }
    }

    public static void UnloadEngines()
    {
        lock (_enginesLock)
        {
            _cachedEngines = null;
            _idleUnloadTimer?.Dispose();
            _idleUnloadTimer = null;
        }

        // Агрессивный сборщик мусора для освобождения нативной памяти моделей
        GC.Collect(1, GCCollectionMode.Optimized, false);
    }

    private static async Task<string> RecognizeBestResultAsync(Bitmap bmp, List<OcrEngine> engines)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        var bytes = ms.ToArray();

        using var randomAccessStream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(randomAccessStream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
        }

        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

        var results = new List<(string Text, string LangTag, double Score)>();

        foreach (var engine in engines)
        {
            var res = await engine.RecognizeAsync(softwareBitmap);
            if (res == null)
            {
                continue;
            }

            // Восстанавливаем естественный порядок чтения (сверху вниз, слева направо)
            // на основе точных координат BoundingRect каждого слова
            var recognized = OcrLayoutReconstructor.ReconstructText(res);
            if (string.IsNullOrWhiteSpace(recognized))
            {
                continue;
            }

            double score = EvaluateRecognizedText(recognized, engine.RecognizerLanguage.LanguageTag);
            results.Add((recognized, engine.RecognizerLanguage.LanguageTag, score));
        }

        if (results.Count == 0)
        {
            return string.Empty;
        }

        var best = results.OrderByDescending(r => r.Score).First();
        return best.Text;
    }

    public static double EvaluateRecognizedText(string text, string langTag)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        int cyrillicCount = 0;
        int latinCount = 0;
        int digitsCount = 0;
        int otherCount = 0;

        foreach (var ch in text)
        {
            if ((ch >= 'а' && ch <= 'я') || (ch >= 'А' && ch <= 'Я') || ch == 'ё' || ch == 'Ё')
            {
                cyrillicCount++;
            }
            else if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z'))
            {
                latinCount++;
            }
            else if (char.IsDigit(ch))
            {
                digitsCount++;
            }
            else
            {
                otherCount++;
            }
        }

        bool isRuEngine = langTag.StartsWith("ru", StringComparison.OrdinalIgnoreCase);

        // Если в тексте есть хоть сколько-то кириллических букв:
        if (cyrillicCount > 0)
        {
            if (isRuEngine)
            {
                // Русский движок выдал осмысленные русские слова
                return 10000.0 + (cyrillicCount * 20.0) + (latinCount * 10.0) + digitsCount;
            }
            else
            {
                return 5000.0 + (cyrillicCount * 10.0);
            }
        }

        // Если кириллицы нет ВООБЩЕ (чисто латинский текст)
        if (isRuEngine)
        {
            return (latinCount * 5.0) + digitsCount;
        }
        else
        {
            return 1000.0 + (latinCount * 10.0) + digitsCount;
        }
    }

    private static Bitmap UpscaleBitmap(Bitmap source, float factor)
    {
        int newWidth = (int)(source.Width * factor);
        int newHeight = (int)(source.Height * factor);
        var result = new Bitmap(newWidth, newHeight, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, 0, 0, newWidth, newHeight);
        return result;
    }
}
