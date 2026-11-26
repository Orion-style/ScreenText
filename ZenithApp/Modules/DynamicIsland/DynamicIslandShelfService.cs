using System.IO;
using System.Collections.Specialized;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using ZenithApp.Core.Logging;

namespace ZenithApp.Modules.DynamicIsland;

public enum IslandShelfItemKind
{
    File,
    Folder,
    Text,
    Link
}

public sealed class IslandShelfItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public IslandShelfItemKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public string? TextContent { get; set; }
    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public bool IsFileOrFolder => Kind is IslandShelfItemKind.File or IslandShelfItemKind.Folder;

    [JsonIgnore]
    public string KindGlyph => Kind switch
    {
        IslandShelfItemKind.Folder => "📁",
        IslandShelfItemKind.File => "📄",
        IslandShelfItemKind.Link => "🔗",
        _ => "📝"
    };
}

public sealed class DynamicIslandShelfService
{
    public const int MaxItems = 25;

    private readonly IAppLogger _logger;
    private readonly string? _storageFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _lock = new();
    private readonly List<IslandShelfItem> _items = new();

    public DynamicIslandShelfService(IAppLogger logger, string? storageFilePath = null)
    {
        _logger = logger;
        _storageFilePath = storageFilePath;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() }
        };

        if (!string.IsNullOrWhiteSpace(_storageFilePath))
        {
            Load();
        }
    }

    public event Action? ItemsChanged;

    public IReadOnlyList<IslandShelfItem> GetItems()
    {
        lock (_lock)
        {
            return _items.ToList();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _items.Count;
            }
        }
    }

    public int AddFiles(IEnumerable<string> paths)
    {
        if (paths == null) return 0;

        int addedCount = 0;
        lock (_lock)
        {
            foreach (var rawPath in paths)
            {
                if (string.IsNullOrWhiteSpace(rawPath)) continue;
                var fullPath = rawPath.Trim();

                bool isDir = Directory.Exists(fullPath);
                bool isFile = !isDir && File.Exists(fullPath);
                if (!isDir && !isFile) continue;

                // Deduplicate existing path by moving it to top
                _items.RemoveAll(i => i.IsFileOrFolder &&
                    string.Equals(i.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));

                var title = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = fullPath;
                }

                string subtitle;
                if (isDir)
                {
                    subtitle = "Папка";
                }
                else
                {
                    var info = new FileInfo(fullPath);
                    var ext = string.IsNullOrEmpty(info.Extension) ? "Файл" : info.Extension.TrimStart('.').ToUpperInvariant();
                    subtitle = $"{ext} · {FormatBytes(info.Length)}";
                }

                _items.Insert(0, new IslandShelfItem
                {
                    Id = Guid.NewGuid(),
                    Kind = isDir ? IslandShelfItemKind.Folder : IslandShelfItemKind.File,
                    Title = title,
                    Subtitle = subtitle,
                    FilePath = fullPath,
                    AddedAtUtc = DateTime.UtcNow
                });
                addedCount++;
            }

            TrimToMaxLocked();
            if (addedCount > 0)
            {
                SaveLocked();
            }
        }

        if (addedCount > 0)
        {
            ItemsChanged?.Invoke();
        }

        return addedCount;
    }

    public bool AddText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return false;

        var trimmed = rawText.Trim();
        var isLink = Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                     (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        var singleLine = trimmed
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();

        var title = singleLine.Length > 52 ? singleLine[..52] + "..." : singleLine;
        var subtitle = isLink
            ? $"Ссылка · {uri!.Host}"
            : $"Текст · {trimmed.Length} симв.";

        lock (_lock)
        {
            _items.RemoveAll(i => !i.IsFileOrFolder &&
                string.Equals(i.TextContent, rawText, StringComparison.Ordinal));

            _items.Insert(0, new IslandShelfItem
            {
                Id = Guid.NewGuid(),
                Kind = isLink ? IslandShelfItemKind.Link : IslandShelfItemKind.Text,
                Title = title,
                Subtitle = subtitle,
                TextContent = rawText,
                AddedAtUtc = DateTime.UtcNow
            });

            TrimToMaxLocked();
            SaveLocked();
        }

        ItemsChanged?.Invoke();
        return true;
    }

    public bool RemoveItem(Guid id)
    {
        bool removed;
        lock (_lock)
        {
            removed = _items.RemoveAll(i => i.Id == id) > 0;
            if (removed)
            {
                SaveLocked();
            }
        }

        if (removed)
        {
            ItemsChanged?.Invoke();
        }

        return removed;
    }

    public void Clear()
    {
        bool hadItems;
        lock (_lock)
        {
            hadItems = _items.Count > 0;
            _items.Clear();
            if (hadItems)
            {
                SaveLocked();
            }
        }

        if (hadItems)
        {
            ItemsChanged?.Invoke();
        }
    }

    public int PruneMissingFiles()
    {
        int removed;
        lock (_lock)
        {
            removed = _items.RemoveAll(i =>
                i.IsFileOrFolder &&
                (string.IsNullOrWhiteSpace(i.FilePath) || (!File.Exists(i.FilePath) && !Directory.Exists(i.FilePath))));

            if (removed > 0)
            {
                SaveLocked();
            }
        }

        if (removed > 0)
        {
            ItemsChanged?.Invoke();
        }

        return removed;
    }

    public static DataObject? CreateDataObjectForItem(IslandShelfItem item)
    {
        if (item == null) return null;

        if (item.IsFileOrFolder && !string.IsNullOrWhiteSpace(item.FilePath) &&
            (File.Exists(item.FilePath) || Directory.Exists(item.FilePath)))
        {
            var data = new DataObject();
            var list = new StringCollection { item.FilePath };
            data.SetFileDropList(list);
            return data;
        }

        if (!item.IsFileOrFolder && !string.IsNullOrEmpty(item.TextContent))
        {
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, item.TextContent);
            data.SetData(DataFormats.Text, item.TextContent);
            return data;
        }

        return null;
    }

    public DataObject? CreateDataObjectForAllFiles()
    {
        List<string> paths;
        lock (_lock)
        {
            paths = _items
                .Where(i => i.IsFileOrFolder && !string.IsNullOrWhiteSpace(i.FilePath) &&
                            (File.Exists(i.FilePath) || Directory.Exists(i.FilePath)))
                .Select(i => i.FilePath!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (paths.Count == 0) return null;

        var data = new DataObject();
        var list = new StringCollection();
        list.AddRange(paths.ToArray());
        data.SetFileDropList(list);
        return data;
    }

    public DataObject? CreateDataObjectForAllText()
    {
        List<string> texts;
        lock (_lock)
        {
            texts = _items
                .Where(i => !i.IsFileOrFolder && !string.IsNullOrEmpty(i.TextContent))
                .Select(i => i.TextContent!)
                .ToList();
        }

        if (texts.Count == 0) return null;

        var joined = string.Join(Environment.NewLine, texts);
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, joined);
        data.SetData(DataFormats.Text, joined);
        return data;
    }

    private void TrimToMaxLocked()
    {
        if (_items.Count > MaxItems)
        {
            _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        }
    }

    private void Load()
    {
        if (string.IsNullOrWhiteSpace(_storageFilePath) || !File.Exists(_storageFilePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_storageFilePath);
            var loaded = JsonSerializer.Deserialize<List<IslandShelfItem>>(json, _jsonOptions);
            if (loaded == null) return;

            lock (_lock)
            {
                _items.Clear();
                foreach (var item in loaded)
                {
                    if (item.IsFileOrFolder)
                    {
                        if (!string.IsNullOrWhiteSpace(item.FilePath) &&
                            (File.Exists(item.FilePath) || Directory.Exists(item.FilePath)))
                        {
                            _items.Add(item);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(item.TextContent))
                    {
                        _items.Add(item);
                    }
                }

                TrimToMaxLocked();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[DynamicIsland] Failed to load shelf items: {ex.Message}");
        }
    }

    private void SaveLocked()
    {
        if (string.IsNullOrWhiteSpace(_storageFilePath)) return;

        try
        {
            var dir = Path.GetDirectoryName(_storageFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_items, _jsonOptions);
            File.WriteAllText(_storageFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[DynamicIsland] Failed to save shelf items: {ex.Message}");
        }
    }

    internal static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} Б";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} КБ";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} МБ";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} ГБ";
    }
}
