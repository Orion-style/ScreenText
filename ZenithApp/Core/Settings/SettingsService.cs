using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZenithApp.Core.Logging;

namespace ZenithApp.Core.Settings;

public interface ISettingsService
{
    AppSettings Current { get; }
    string SettingsFilePath { get; }
    string SettingsDirectory { get; }
    void Load();
    void Save();
    event Action<AppSettings>? SettingsChanged;
}

public sealed class SettingsService : ISettingsService
{
    private readonly IAppLogger _logger;
    private readonly string _settingsFilePath;
    private readonly string _settingsDirectory;
    private readonly JsonSerializerOptions _jsonOptions;
    private AppSettings _current;

    public SettingsService(IAppLogger logger, string? customSettingsPath = null)
    {
        _logger = logger;
        if (customSettingsPath != null)
        {
            _settingsFilePath = customSettingsPath;
            _settingsDirectory = Path.GetDirectoryName(customSettingsPath) ?? AppContext.BaseDirectory;
        }
        else
        {
            _settingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ZenithApp"
            );
            _settingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
        }

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() }
        };

        _current = new AppSettings();
        Load();
    }

    public AppSettings Current => _current;
    public string SettingsFilePath => _settingsFilePath;
    public string SettingsDirectory => _settingsDirectory;

    public event Action<AppSettings>? SettingsChanged;

    public void Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                _logger.LogInfo($"Settings file not found at '{_settingsFilePath}'. Creating default settings.");
                _current = new AppSettings();
                Save();
                return;
            }

            var json = File.ReadAllText(_settingsFilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
            _current = loaded ?? new AppSettings();
            _logger.LogInfo("Settings loaded successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to read settings from '{_settingsFilePath}'. Backing up corrupted file and resetting to defaults.", ex);
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var backupPath = _settingsFilePath + ".corrupted." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    File.Copy(_settingsFilePath, backupPath, overwrite: true);
                }
            }
            catch
            {
                // Ignore backup error
            }

            _current = new AppSettings();
            Save();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            var json = JsonSerializer.Serialize(_current, _jsonOptions);
            // Atomic file write using temporary file to prevent corruption on crash
            var tempPath = _settingsFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsFilePath, overwrite: true);

            SettingsChanged?.Invoke(_current);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to save settings.", ex);
        }
    }
}
