using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ZenithApp.Modules.SmartLinks;

public enum ContentKind
{
    SingleUrl,
    MultipleUrls,
    QrSpecial,
    PlainText
}

public sealed class ContentClassification
{
    public ContentKind Kind { get; }
    public string RawContent { get; }
    public bool IsFromQr { get; }
    public IReadOnlyList<string> Urls { get; }

    public ContentClassification(ContentKind kind, string rawContent, bool isFromQr, IReadOnlyList<string> urls)
    {
        Kind = kind;
        RawContent = rawContent;
        IsFromQr = isFromQr;
        Urls = urls;
    }
}

public static class ContentClassifier
{
    // Регулярное выражение для обнаружения URL (схема http/https или домен с путем)
    private static readonly Regex ExplicitUrlRegex = new(
        @"(?:https?:\/\/)[^\s/$.?#].[^\s]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Домены без схемы, например github.com/user или google.com
    private static readonly Regex ImplicitUrlRegex = new(
        @"\b(?:[a-zA-Z0-9-]+\.)+(?:com|org|net|ru|io|dev|app|ai|info|biz|me|cc|edu|gov)(?:\/[^\s]*)?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ContentClassification Classify(string content, bool isFromQr)
    {
        var trimmed = content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new ContentClassification(ContentKind.PlainText, string.Empty, isFromQr, Array.Empty<string>());
        }

        // Если это QR-код со специальной схемой (wifi, tel, mailto, smsto, geo и т.п.)
        if (isFromQr)
        {
            if (trimmed.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("smsto:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("sms:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("geo:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("otpauth:", StringComparison.OrdinalIgnoreCase))
            {
                return new ContentClassification(ContentKind.QrSpecial, trimmed, isFromQr: true, Array.Empty<string>());
            }
        }

        // Извлекаем допустимые URL
        var urls = ExtractValidHttpUrls(trimmed, isFromQr);

        if (urls.Count == 1)
        {
            return new ContentClassification(ContentKind.SingleUrl, trimmed, isFromQr, urls);
        }

        if (urls.Count > 1)
        {
            return new ContentClassification(ContentKind.MultipleUrls, trimmed, isFromQr, urls);
        }

        return new ContentClassification(ContentKind.PlainText, trimmed, isFromQr, Array.Empty<string>());
    }

    private static List<string> ExtractValidHttpUrls(string text, bool isFromQr)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Поиск ссылок с явным http:// или https://
        foreach (Match match in ExplicitUrlRegex.Matches(text))
        {
            var raw = CleanUrlCandidate(match.Value);
            if (IsValidHttpUrl(raw, out var validatedUrl) && seen.Add(validatedUrl))
            {
                result.Add(validatedUrl);
            }
        }

        // 2. Если явных ссылок не нашлось, проверяем домены без схемы (например example.com/page)
        if (result.Count == 0)
        {
            foreach (Match match in ImplicitUrlRegex.Matches(text))
            {
                var candidate = "https://" + CleanUrlCandidate(match.Value);
                if (IsValidHttpUrl(candidate, out var validatedUrl) && seen.Add(validatedUrl))
                {
                    result.Add(validatedUrl);
                }
            }
        }

        return result;
    }

    private static string CleanUrlCandidate(string url)
    {
        // Убираем точки, запятые, скобки и кавычки на конце ссылки
        return url.TrimEnd('.', ',', ';', ':', ')', ']', '}', '"', '\'');
    }

    public static bool IsValidHttpUrl(string candidate, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                // Проверяем валидность хоста (должна быть точка, отсутствие пробелов)
                if (!string.IsNullOrWhiteSpace(uri.Host) && uri.Host.Contains('.') && !uri.Host.Contains(' '))
                {
                    normalizedUrl = uri.AbsoluteUri;
                    return true;
                }
            }
        }

        return false;
    }

    public static bool SafeOpenInBrowser(string url)
    {
        if (!IsValidHttpUrl(url, out var safeUrl))
        {
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = safeUrl,
                UseShellExecute = true
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string FormatDomainForToast(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var host = uri.Host;
            var display = url;
            if (display.Length > 40)
            {
                // Обрезаем из середины, сохраняя домен
                var prefix = display[..Math.Min(25, display.Length)];
                var suffix = display[^10..];
                display = $"{prefix}...{suffix}";
            }
            return $"{host} ({display})";
        }
        return url;
    }
}
