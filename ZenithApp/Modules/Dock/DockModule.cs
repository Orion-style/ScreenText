using System.IO;
using System.Windows;
using System.Windows.Input;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;

namespace ZenithApp.Modules.Dock;

public sealed class DockModule : BaseFeatureModule
{
    public const string ModuleId = "dock";

    private readonly ISettingsService _settingsService;
    private readonly IHotkeyService _hotkeyService;

    private DockWindow? _dockWindow;
    private DockTriggerWindow? _triggerWindow;
    private readonly object _lock = new();

    public event Action? ItemsChanged;

    public DockModule(
        IAppLogger logger,
        ISettingsService settingsService,
        IHotkeyService hotkeyService) : base(logger)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
    }

    public override string Id => ModuleId;
    public override string Name => "Док-панель (macOS style)";
    public override string Description => "Полупрозрачная скруглённая панель по центру экрана для быстрого запуска и переключения окон.";

    public DockSettings Settings => _settingsService.Current.Dock;

    protected override Task OnEnableAsync()
    {
        Logger.LogInfo("[DockModule] Включение модуля...");

        // Ensure default items if empty
        EnsureDefaultItems();

        // Register hotkey
        RegisterDockHotkey();

        // Create UI elements on WPF Dispatcher
        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                if (_dockWindow == null)
                {
                    _dockWindow = new DockWindow(this, Logger);
                }

                if (Settings.AutoHide)
                {
                    _dockWindow.HideDockAnimated();

                    if (_triggerWindow == null)
                    {
                        _triggerWindow = new DockTriggerWindow(this);
                        _triggerWindow.Show();
                    }
                }
                else
                {
                    _dockWindow.Show();
                    _dockWindow.ShowDockAnimated();
                }
            }
        });

        Logger.LogInfo("[DockModule] Успешно запущен.");
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        Logger.LogInfo("[DockModule] Отключение модуля...");

        UnregisterDockHotkey();

        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                if (_triggerWindow != null)
                {
                    _triggerWindow.Close();
                    _triggerWindow = null;
                }

                if (_dockWindow != null)
                {
                    _dockWindow.Close();
                    _dockWindow = null;
                }
            }

            IconExtractionService.ClearCache();
        });

        Logger.LogInfo("[DockModule] Остановлен. Все ресурсы и окна освобождены.");
        return Task.CompletedTask;
    }

    private void EnsureDefaultItems()
    {
        if (Settings.Items.Count == 0)
        {
            // Add common standard apps if they exist
            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var notepad = Path.Combine(windir, "System32", "notepad.exe");
            var explorer = Path.Combine(windir, "explorer.exe");
            var calc = Path.Combine(windir, "System32", "calc.exe");

            if (File.Exists(notepad)) AddItem(notepad, "Блокнот");
            if (File.Exists(explorer)) AddItem(explorer, "Проводник");
            if (File.Exists(calc)) AddItem(calc, "Калькулятор");
        }
    }

    public void AddItem(string path, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var resolved = IconExtractionService.ResolveShortcutTarget(path);
        var itemTitle = title ?? Path.GetFileNameWithoutExtension(resolved);
        if (string.IsNullOrWhiteSpace(itemTitle)) itemTitle = Path.GetFileName(path);

        var item = new DockItem
        {
            TargetPath = path,
            Title = itemTitle
        };

        Settings.Items.Add(item);
        _settingsService.Save();

        _dockWindow?.ReloadItems();
        ItemsChanged?.Invoke();
    }

    public void RemoveItem(DockItem item)
    {
        Settings.Items.Remove(item);
        _settingsService.Save();

        _dockWindow?.ReloadItems();
        ItemsChanged?.Invoke();
    }

    public void MoveItem(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Settings.Items.Count || newIndex < 0 || newIndex >= Settings.Items.Count)
            return;

        var item = Settings.Items[oldIndex];
        Settings.Items.RemoveAt(oldIndex);
        Settings.Items.Insert(newIndex, item);
        _settingsService.Save();

        _dockWindow?.ReloadItems();
        ItemsChanged?.Invoke();
    }

    public void OnEdgeTriggerActivated()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_dockWindow != null)
            {
                _dockWindow.Show();
                _dockWindow.ShowDockAnimated();
            }
        });
    }

    public void ToggleDock()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (ProcessInteractionService.IsForegroundWindowFullScreen())
            {
                return;
            }

            if (_dockWindow != null)
            {
                _dockWindow.Show();
                _dockWindow.ToggleDockVisibility();
            }
        });
    }

    public void ApplySettingsChanges()
    {
        _settingsService.Save();

        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (!IsEnabled) return;

            UnregisterDockHotkey();
            RegisterDockHotkey();

            if (_dockWindow != null)
            {
                _dockWindow.ReloadItems();
                _dockWindow.UpdatePosition();

                if (Settings.AutoHide)
                {
                    if (_triggerWindow == null)
                    {
                        _triggerWindow = new DockTriggerWindow(this);
                        _triggerWindow.Show();
                    }
                    _triggerWindow.UpdatePosition();
                }
                else
                {
                    _triggerWindow?.Close();
                    _triggerWindow = null;
                    _dockWindow.ShowDockAnimated();
                }
            }
        });
    }

    private void RegisterDockHotkey()
    {
        try
        {
            if (Enum.TryParse<KeyModifiers>(Settings.HotkeyModifiers, out var mods) &&
                Enum.TryParse<Key>(Settings.HotkeyKey, out var key))
            {
                _hotkeyService.RegisterHotkey("ZenithDockToggle", mods, key, ToggleDock);
            }
            else
            {
                _hotkeyService.RegisterHotkey("ZenithDockToggle", KeyModifiers.Control | KeyModifiers.Alt, Key.D, ToggleDock);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[DockModule] Не удалось зарегистрировать хоткей дока: {ex.Message}");
        }
    }

    private void UnregisterDockHotkey()
    {
        _hotkeyService.UnregisterHotkey("ZenithDockToggle");
    }
}
