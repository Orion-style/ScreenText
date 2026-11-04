using System.IO;
using System.Text;

namespace ZenithApp.Modules.QuickLook;

public sealed class TextFilePreviewResult
{
    public string Content { get; }
    public string EncodingName { get; }
    public bool IsTruncated { get; }
    public long OriginalSizeBytes { get; }

    public TextFilePreviewResult(string content, string encodingName, bool isTruncated, long originalSizeBytes)
    {
        Content = content;
        EncodingName = encodingName;
        IsTruncated = isTruncated;
        OriginalSizeBytes = originalSizeBytes;
    }
}

public static class TextFileReader
{
    public static async Task<TextFilePreviewResult> ReadTextPreviewAsync(string filePath, int maxBytes)
    {
        var fi = new FileInfo(filePath);
        long length = fi.Length;
        int bytesToRead = (int)Math.Min(length, maxBytes);
        bool isTruncated = length > maxBytes;

        byte[] buffer = new byte[bytesToRead];
        await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            int totalRead = 0;
            while (totalRead < bytesToRead)
            {
                int read = await fs.ReadAsync(buffer.AsMemory(totalRead, bytesToRead - totalRead));
                if (read == 0) break;
                totalRead += read;
            }
        }

        var encoding = DetectEncoding(buffer);
        string text = encoding.GetString(buffer);

        return new TextFilePreviewResult(text, encoding.EncodingName, isTruncated, length);
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        // 1. Check Byte Order Mark (BOM)
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8;
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode; // UTF-16 LE
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode; // UTF-16 BE
        }

        // 2. Check if valid UTF-8
        if (IsValidUtf8(bytes))
        {
            return new UTF8Encoding(false);
        }

        // 3. Fallback to Windows-1251 (Cyrillic) or Default ANSI
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251);
        }
        catch
        {
            return Encoding.Default;
        }
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        int i = 0;
        int len = bytes.Length;
        while (i < len)
        {
            byte b = bytes[i];
            if ((b & 0x80) == 0)
            {
                i++;
            }
            else if ((b & 0xE0) == 0xC0)
            {
                if (i + 1 >= len || (bytes[i + 1] & 0xC0) != 0x80) return false;
                i += 2;
            }
            else if ((b & 0xF0) == 0xE0)
            {
                if (i + 2 >= len || (bytes[i + 1] & 0xC0) != 0x80 || (bytes[i + 2] & 0xC0) != 0x80) return false;
                i += 3;
            }
            else if ((b & 0xF8) == 0xF0)
            {
                if (i + 3 >= len || (bytes[i + 1] & 0xC0) != 0x80 || (bytes[i + 2] & 0xC0) != 0x80 || (bytes[i + 3] & 0xC0) != 0x80) return false;
                i += 4;
            }
            else
            {
                return false;
            }
        }
        return true;
    }
}
