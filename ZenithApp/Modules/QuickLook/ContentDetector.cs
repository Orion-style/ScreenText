using System.IO;

namespace ZenithApp.Modules.QuickLook;

public enum QuickLookContentType
{
    Image,
    Pdf,
    Video,
    Audio,
    Text,
    Archive,
    GenericCard
}

public static class ContentDetector
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif"
    };

    private static readonly HashSet<string> PdfExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".m4v"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".m4a", ".ogg", ".wma", ".aac"
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".nupkg", ".jar", ".apk"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".xml", ".csv", ".log", ".ini", ".yaml", ".yml",
        ".cs", ".py", ".js", ".ts", ".html", ".htm", ".css", ".scss", ".sass", ".less",
        ".cpp", ".c", ".h", ".hpp", ".java", ".kt", ".kts", ".rs", ".go", ".php",
        ".sql", ".sh", ".bash", ".ps1", ".psm1", ".bat", ".cmd", ".config", ".xaml",
        ".proto", ".svg", ".properties", ".gradle", ".cmake", ".gitignore", ".env"
    };

    public static QuickLookContentType DetectContentType(string filePath, QuickLookSettings settings)
    {
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
        {
            var fileName = Path.GetFileName(filePath);
            if (string.Equals(fileName, ".gitignore", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, ".env", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "Dockerfile", StringComparison.OrdinalIgnoreCase))
            {
                return settings.EnableText ? QuickLookContentType.Text : QuickLookContentType.GenericCard;
            }
        }

        if (settings.EnableImages && ImageExtensions.Contains(ext))
        {
            return QuickLookContentType.Image;
        }

        if (settings.EnablePdf && PdfExtensions.Contains(ext))
        {
            return QuickLookContentType.Pdf;
        }

        if (settings.EnableMedia && VideoExtensions.Contains(ext))
        {
            return QuickLookContentType.Video;
        }

        if (settings.EnableMedia && AudioExtensions.Contains(ext))
        {
            return QuickLookContentType.Audio;
        }

        if (settings.EnableArchives && ArchiveExtensions.Contains(ext))
        {
            return QuickLookContentType.Archive;
        }

        if (settings.EnableText && TextExtensions.Contains(ext))
        {
            return QuickLookContentType.Text;
        }

        return QuickLookContentType.GenericCard;
    }
}
