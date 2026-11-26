using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using ZenithApp.Core.Logging;

namespace ZenithApp.Modules.StickyNotes;

public interface IStickyNotesStorageService
{
    string StorageFilePath { get; }
    IReadOnlyList<StickyNoteItem> Load();
    void Save(IEnumerable<StickyNoteItem> notes);
}

public sealed class StickyNotesDocument
{
    public int Version { get; set; } = 1;
    public List<StickyNoteItem> Notes { get; set; } = new();
}

public sealed class StickyNotesStorageService : IStickyNotesStorageService
{
    private readonly IAppLogger _logger;
    private readonly string _storageDirectory;
    private readonly string _storageFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _sync = new();

    public StickyNotesStorageService(IAppLogger logger, string? customStoragePath = null)
    {
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(customStoragePath))
        {
            _storageFilePath = customStoragePath;
            _storageDirectory = Path.GetDirectoryName(customStoragePath) ?? AppContext.BaseDirectory;
        }
        else
        {
            _storageDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ZenithApp");
            _storageFilePath = Path.Combine(_storageDirectory, "sticky_notes.json");
        }

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    public string StorageFilePath => _storageFilePath;

    public IReadOnlyList<StickyNoteItem> Load()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_storageFilePath))
                {
                    return Array.Empty<StickyNoteItem>();
                }

                var json = File.ReadAllText(_storageFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return Array.Empty<StickyNoteItem>();
                }

                var document = JsonSerializer.Deserialize<StickyNotesDocument>(json, _jsonOptions);
                if (document?.Notes == null)
                {
                    return Array.Empty<StickyNoteItem>();
                }

                var seenIds = new HashSet<Guid>();
                var sanitized = new List<StickyNoteItem>(document.Notes.Count);
                foreach (var note in document.Notes)
                {
                    if (note == null) continue;
                    if (note.Id == Guid.Empty || !seenIds.Add(note.Id))
                    {
                        note.Id = Guid.NewGuid();
                        seenIds.Add(note.Id);
                    }

                    note.Content ??= string.Empty;
                    note.ColorHex = StickyNotePalette.Resolve(note.ColorHex).Id;
                    note.Opacity = double.IsFinite(note.Opacity) ? Math.Clamp(note.Opacity, 0.35, 1.0) : 0.92;
                    note.FontSize = double.IsFinite(note.FontSize) ? Math.Clamp(note.FontSize, 10, 28) : 14;
                    sanitized.Add(note);
                }

                return sanitized;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StickyNotes] Failed to read notes from '{_storageFilePath}'. Backing up corrupted file.", ex);
                TryBackupCorruptedFile();
                return Array.Empty<StickyNoteItem>();
            }
        }
    }

    public void Save(IEnumerable<StickyNoteItem> notes)
    {
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(_storageDirectory);
                var doc = new StickyNotesDocument
                {
                    Version = 1,
                    Notes = notes.Select(n => n.Clone()).ToList()
                };

                var json = JsonSerializer.Serialize(doc, _jsonOptions);
                var tempPath = _storageFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _storageFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[StickyNotes] Failed to save notes to '{_storageFilePath}'.", ex);
            }
        }
    }

    private void TryBackupCorruptedFile()
    {
        try
        {
            if (File.Exists(_storageFilePath))
            {
                var backupPath = _storageFilePath + ".corrupted." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Copy(_storageFilePath, backupPath, overwrite: true);
            }
        }
        catch
        {
            // Ignore backup error
        }
    }
}
