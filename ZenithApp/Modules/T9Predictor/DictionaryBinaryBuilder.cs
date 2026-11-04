using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZenithApp.Modules.T9Predictor;

public static class DictionaryBinaryBuilder
{
    public static void BuildBinaryFile(IEnumerable<(string Word, uint Frequency)> words, string outputPath)
    {
        // 1. Filter, deduplicate, and sort alphabetically
        var dict = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in words)
        {
            string clean = item.Word.Trim().ToLowerInvariant();
            if (clean.Length < 2) continue;

            if (dict.TryGetValue(clean, out uint existingFreq))
            {
                if (item.Frequency > existingFreq) dict[clean] = item.Frequency;
            }
            else
            {
                dict[clean] = item.Frequency;
            }
        }

        var sortedList = new List<(string Word, uint Frequency)>(dict.Count);
        foreach (var kvp in dict)
        {
            sortedList.Add((kvp.Key, kvp.Value));
        }

        // Sort alphabetically strictly for binary search
        sortedList.Sort((a, b) => string.Compare(a.Word, b.Word, StringComparison.OrdinalIgnoreCase));

        // 2. Write binary structure
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);

        // Header: Magic "ZNDI", Version (1), Count
        bw.Write(Encoding.ASCII.GetBytes("ZNDI"));
        bw.Write((int)1);
        bw.Write(sortedList.Count);

        // Prepare strings buffer
        using var stringsMs = new MemoryStream();
        using var stringsBw = new BinaryWriter(stringsMs);

        foreach (var entry in sortedList)
        {
            int strOffset = (int)stringsMs.Position;
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(entry.Word);
            ushort len = (ushort)utf8Bytes.Length;

            // Write index entry: 4 bytes Offset, 2 bytes Length, 4 bytes Frequency
            bw.Write(strOffset);
            bw.Write(len);
            bw.Write(entry.Frequency);

            stringsBw.Write(utf8Bytes);
        }

        // Append strings section
        byte[] allStringsBytes = stringsMs.ToArray();
        bw.Write(allStringsBytes);
    }
}
