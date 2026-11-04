using System;
using System.IO;
using System.Threading.Tasks;
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

        // Crash Guard: если при прошлом падении панель задач осталась в изменённом состоянии, восстанавливаем её
        CheckCrashGuard();
    }

    public override string Id => ModuleId;
    public override string Name => "Док-панель (macOS style)";
    public override string Description => "Полупрозрачная скруглённая панель по центру экрана для быстрого запуска и переключения окон.";

    public DockSettings Settings => _settingsService.Current.Dock;

    private void CheckCrashGuard()
    {
        try
        {
            if (Settings.TaskbarStateModified && Settings.OriginalTaskbarState.HasValue)
            {
                Logger.LogWarning($"[DockModule] Crash Guard: обнаружен флаг несохранённого состояния панели задач (было {Settings.OriginalTaskbarState.Value}). Восстанавливаем...");
                WindowsTaskbarService.RestoreState(Settings.OriginalTaskbarState.Value, Logger);
                Settings.TaskbarStateModified = false;
                _settingsService.Save();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[DockModule] Crash Guard: ошибка при восстановлении панели задач: {ex.Message}", ex);
        }
    }

    protected override Task OnEnableAsync()
    {
        Logger.LogInfo("[DockModule] Включение модуля...");

        EnsureDefaultItems();
        RegisterDockHotkey();

        // Если включен режим «Заменять панель задач Windows», запоминаем исходное состояние и включаем автоскрытие
        if (Settings.ReplaceWindowsTaskbar)
        {
            ApplyTaskbarReplacement(true);
        }

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
                    // Режим по умолчанию «Всегда виден»
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

        // Восстанавливаем исходное состояние панели задач Windows
        if (Settings.ReplaceWindowsTaskbar || Settings.TaskbarStateModified)
        {
            ApplyTaskbarReplacement(false);
        }

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

    public void ApplyTaskbarReplacement(bool enable)
    {
        try
        {
            if (enable)
            {
                // Запоминаем текущее состояние, если оно ещё не было зафиксировано
                if (!Settings.OriginalTaskbarState.HasValue)
                {
                    Settings.OriginalTaskbarState = WindowsTaskbarService.GetTaskbarState();
                    Settings.TaskbarStateModified = true;
                    _settingsService.Save();
                }

                WindowsTaskbarService.SetAutoHide(true, Logger);
            }
            else
            {
                if (Settings.OriginalTaskbarState.HasValue)
                {
                    WindowsTaskbarService.RestoreState(Settings.OriginalTaskbarState.Value, Logger);
                }
                else
                {
                    // Если значение не было сохранено, выключаем автоскрытие
                    WindowsTaskbarService.SetAutoHide(false, Logger);
                }

                Settings.TaskbarStateModified = false;
                Settings.OriginalTaskbarState = null;
                _settingsService.Save();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[DockModule] Ошибка переключения режима панели задач: {ex.Message}", ex);
        }
    }

    private void EnsureDefaultItems()
    {
        if (Settings.Items.Count == 0)
        {
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

            // Обработка настройки «Заменять панель задач Windows»
            if (Settings.ReplaceWindowsTaskbar)
            {
                ApplyTaskbarReplacement(true);
            }
            else
            {
                ApplyTaskbarReplacement(false);
            }

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
