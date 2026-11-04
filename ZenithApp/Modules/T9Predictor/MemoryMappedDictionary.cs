using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace ZenithApp.Modules.T9Predictor;

/// <summary>
/// High-performance memory-mapped binary dictionary.
/// Binary layout:
/// - 4 bytes: Magic ("ZNDI")
/// - 4 bytes: Version (1)
/// - 4 bytes: Entry count (N)
/// - Array of Index Entries [N]:
///     - 4 bytes: Offset in strings section
///     - 2 bytes: Length of word
///     - 4 bytes: Frequency (UInt32)
/// - Strings section (UTF-8 encoded words)
/// </summary>
public sealed class MemoryMappedDictionary : IWordDictionarySource
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly int _entryCount;
    private readonly int _stringsOffset;
    private bool _disposed;

    public string LanguageCode { get; }

    public MemoryMappedDictionary(string filePath, string languageCode)
    {
        LanguageCode = languageCode;
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Dictionary file not found: {filePath}");
        }

        var fi = new FileInfo(filePath);
        _mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _accessor = _mmf.CreateViewAccessor(0, fi.Length, MemoryMappedFileAccess.Read);

        // Header check
        byte[] magic = new byte[4];
        _accessor.ReadArray(0, magic, 0, 4);
        if (Encoding.ASCII.GetString(magic) != "ZNDI")
        {
            throw new InvalidDataException("Invalid dictionary header format.");
        }

        int version = _accessor.ReadInt32(4);
        if (version != 1)
        {
            throw new InvalidDataException($"Unsupported dictionary version: {version}");
        }

        _entryCount = _accessor.ReadInt32(8);
        // Header (12 bytes) + IndexEntries (N * 10 bytes)
        _stringsOffset = 12 + (_entryCount * 10);
    }

    public string? FindBestMatch(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return null;

        var matches = FindMatches(prefix, 1);
        return matches.Count > 0 ? matches[0] : null;
    }

    public IReadOnlyList<string> FindMatches(string prefix, int maxResults)
    {
        if (_disposed || _entryCount == 0 || string.IsNullOrEmpty(prefix))
        {
            return Array.Empty<string>();
        }

        prefix = prefix.ToLowerInvariant();

        // 1. Binary search to find the lower bound of prefix
        int low = 0;
        int high = _entryCount - 1;
        int firstIndex = -1;

        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            string word = ReadWordAt(mid);

            int cmp = string.Compare(word, 0, prefix, 0, Math.Min(word.Length, prefix.Length), StringComparison.OrdinalIgnoreCase);

            if (cmp >= 0)
            {
                if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    firstIndex = mid;
                    high = mid - 1; // Look left for first match
                }
                else
                {
                    high = mid - 1;
                }
            }
            else
            {
                low = mid + 1;
            }
        }

        if (firstIndex == -1)
        {
            return Array.Empty<string>();
        }

        // 2. Collect candidate matches strictly longer than prefix and rank by frequency
        var candidates = new List<(string Word, uint Frequency)>();
        int cur = firstIndex;

        while (cur < _entryCount)
        {
            string word = ReadWordAt(cur);
            if (!word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (word.Length > prefix.Length)
            {
                uint freq = ReadFrequencyAt(cur);
                candidates.Add((word, freq));
            }

            cur++;
            // Scan up to reasonable prefix cluster window to bound latency
            if (candidates.Count >= 100) break;
        }

        if (candidates.Count == 0)
        {
            return Array.Empty<string>();
        }

        // Sort by frequency descending, then shorter word length
        candidates.Sort((a, b) =>
        {
            int fc = b.Frequency.CompareTo(a.Frequency);
            if (fc != 0) return fc;
            return a.Word.Length.CompareTo(b.Word.Length);
        });

        int take = Math.Min(maxResults, candidates.Count);
        var result = new List<string>(take);
        for (int i = 0; i < take; i++)
        {
            result.Add(candidates[i].Word);
        }

        return result;
    }

    private string ReadWordAt(int index)
    {
        long indexOffset = 12 + (index * 10);
        int strRelOffset = _accessor.ReadInt32(indexOffset);
        ushort len = _accessor.ReadUInt16(indexOffset + 4);

        long absOffset = _stringsOffset + strRelOffset;
        byte[] buffer = new byte[len];
        _accessor.ReadArray(absOffset, buffer, 0, len);
        return Encoding.UTF8.GetString(buffer);
    }

    private uint ReadFrequencyAt(int index)
    {
        long indexOffset = 12 + (index * 10);
        return _accessor.ReadUInt32(indexOffset + 6);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _accessor.Dispose();
        _mmf.Dispose();
    }
}
