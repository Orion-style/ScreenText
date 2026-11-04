using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Input;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;

namespace ZenithApp.Modules.T9Predictor;

public sealed class T9PredictorModule : BaseFeatureModule
{
    public const string ModuleId = "t9_predictor";
    private const string PauseHotkeyId = "Zenith_T9_Pause";

    private const int VK_BACK = 0x08;
    private const int VK_TAB = 0x09;
    private const int VK_RETURN = 0x0D;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SPACE = 0x20;
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;

    private readonly IKeyboardHookService _hookService;
    private readonly IHotkeyService _hotkeyService;
    private readonly ISettingsService _settingsService;

    private IDisposable? _hookSubscription;
    private readonly BlockingCollection<KeyboardHookEventArgs> _eventQueue = new();
    private Thread? _workerThread;
    private CancellationTokenSource? _workerCts;

    private readonly List<IWordDictionarySource> _dictionaries = new();
    private T9InlineHintOverlay? _hintOverlay;
    private readonly object _hintLock = new();

    private readonly StringBuilder _currentWord = new();
    private string? _currentSuggestionSuffix;
    private bool _isPaused;
    private nint _lastActiveWindow = nint.Zero;

    // Cache of windows where caret cannot be resolved (to avoid repeated probing)
    private readonly HashSet<string> _incompatibleWindowClasses = new(StringComparer.OrdinalIgnoreCase);

    public T9PredictorModule(
        IAppLogger logger, 
        IKeyboardHookService hookService,
        IHotkeyService hotkeyService,
        ISettingsService settingsService) 
        : base(logger)
    {
        _hookService = hookService;
        _hotkeyService = hotkeyService;
        _settingsService = settingsService;
    }

    public override string Id => ModuleId;
    public override string Name => "T9 / Умное автодополнение";
    public override string Description => "Инлайн-подсказка продолжения набираемого слова серым текстом у курсора по клавише Tab с офлайн-словарём.";

    public T9PredictorSettings Settings => _settingsService.Current.T9Predictor;
    public bool IsPaused => _isPaused;

    protected override Task OnEnableAsync()
    {
        Logger.LogInfo("[T9PredictorModule] Инициализация бинарных словарей и фонового потока...");

        string dictDir = Path.Combine(AppContext.BaseDirectory, "Resources", "Dictionaries");
        BuiltinWordData.EnsureDictionariesExist(dictDir);

        LoadDictionaries(dictDir);

        _workerCts = new CancellationTokenSource();
        _workerThread = new Thread(ProcessInputWorker)
        {
            IsBackground = true,
            Name = "Zenith_T9Predictor_Worker"
        };
        _workerThread.Start();

        _hookSubscription = _hookService.Subscribe(OnLowLevelKeyboardInput);
        RegisterPauseHotkey();

        Logger.LogInfo("[T9PredictorModule] Модуль успешно запущен.");
        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        Logger.LogInfo("[T9PredictorModule] Остановка модуля...");

        UnregisterPauseHotkey();

        _hookSubscription?.Dispose();
        _hookSubscription = null;

        _workerCts?.Cancel();
        _workerThread?.Join(500);
        _workerCts?.Dispose();
        _workerCts = null;

        UnloadDictionaries();
        HideHintImmediate();

        Logger.LogInfo("[T9PredictorModule] Модуль остановлен. Все ресурсы освобождены.");
        return Task.CompletedTask;
    }

    public void TogglePause()
    {
        _isPaused = !_isPaused;
        ResetBuffer();
        Logger.LogInfo($"[T9PredictorModule] Режим паузы: {(_isPaused ? "Включен (пауза)" : "Выключен (работает)")}");
    }

    public void RegisterPauseHotkey()
    {
        _hotkeyService.UnregisterHotkey(PauseHotkeyId);
        _hotkeyService.RegisterHotkey(PauseHotkeyId, Settings.PauseHotkeyModifiers, Settings.PauseHotkeyKey, TogglePause);
    }

    public void UnregisterPauseHotkey()
    {
        _hotkeyService.UnregisterHotkey(PauseHotkeyId);
    }

    private void LoadDictionaries(string dictDir)
    {
        UnloadDictionaries();
        try
        {
            if (Settings.EnableRussian)
            {
                string ruPath = Path.Combine(dictDir, "ru.dict");
                if (File.Exists(ruPath)) _dictionaries.Add(new MemoryMappedDictionary(ruPath, "ru"));
            }
            if (Settings.EnableEnglish)
            {
                string enPath = Path.Combine(dictDir, "en.dict");
                if (File.Exists(enPath)) _dictionaries.Add(new MemoryMappedDictionary(enPath, "en"));
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[T9PredictorModule] Ошибка при открытии словарей: {ex.Message}", ex);
        }
    }

    private void UnloadDictionaries()
    {
        foreach (var dict in _dictionaries)
        {
            dict.Dispose();
        }
        _dictionaries.Clear();
    }

    private bool OnLowLevelKeyboardInput(KeyboardHookEventArgs e)
    {
        if (_isPaused) return false;

        // Ignore injected events
        if (e.IsInjected || e.ExtraInfo == 0x5A454E54) return false;

        // Accept key check when suggestion is visible
        if (e.IsKeyDown && !string.IsNullOrEmpty(_currentSuggestionSuffix))
        {
            if (IsAcceptKey(e.VirtualCode))
            {
                AcceptCurrentSuggestion();
                return true; // Suppress accept key
            }
            else if (e.VirtualCode == VK_ESCAPE)
            {
                HideHintImmediate();
                return true; // Suppress Esc closing suggestion
            }
        }

        // Hot fast path: only forward KeyDown events to background queue
        if (e.IsKeyDown)
        {
            _eventQueue.TryAdd(e);
        }

        return false;
    }

    private bool IsAcceptKey(int vk)
    {
        return Settings.AcceptKey switch
        {
            AcceptKeyOption.Tab => vk == VK_TAB,
            AcceptKeyOption.RightArrow => vk == VK_RIGHT,
            AcceptKeyOption.Enter => vk == VK_RETURN,
            _ => vk == VK_TAB
        };
    }

    private void ProcessInputWorker()
    {
        var token = _workerCts?.Token ?? CancellationToken.None;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_eventQueue.TryTake(out var e, 100, token))
                {
                    HandleKeyEventInWorker(e);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }
        }
    }

    private void HandleKeyEventInWorker(KeyboardHookEventArgs e)
    {
        var fgHwnd = KeyboardLayoutHelper.GetForegroundWindow();
        if (fgHwnd == nint.Zero)
        {
            ResetBuffer();
            return;
        }

        if (fgHwnd != _lastActiveWindow)
        {
            _lastActiveWindow = fgHwnd;
            ResetBuffer();
        }

        // Ignore if modifier keys pressed
        if (KeyboardLayoutHelper.AreModifiersPressed())
        {
            ResetBuffer();
            return;
        }

        // Backspace removes last character
        if (e.VirtualCode == VK_BACK)
        {
            if (_currentWord.Length > 0)
            {
                _currentWord.Length--;
                EvaluateWordSuggestion(fgHwnd);
            }
            else
            {
                ResetBuffer();
            }
            return;
        }

        // Delimiting keys reset word
        if (e.VirtualCode == VK_SPACE || e.VirtualCode == VK_RETURN || e.VirtualCode == VK_TAB || 
            e.VirtualCode == VK_ESCAPE || e.VirtualCode == VK_LEFT || e.VirtualCode == VK_RIGHT || 
            e.VirtualCode == VK_UP || e.VirtualCode == VK_DOWN)
        {
            ResetBuffer();
            return;
        }

        // Translate key to character based on active window's keyboard layout
        uint threadId = KeyboardLayoutHelper.GetWindowThreadProcessId(fgHwnd, out _);
        nint hkl = KeyboardLayoutHelper.GetKeyboardLayout(threadId);
        char? ch = KeyboardLayoutHelper.TranslateKeyToChar((uint)e.VirtualCode, (uint)e.ScanCode, hkl);

        if (ch.HasValue)
        {
            char c = ch.Value;
            if (char.IsLetter(c))
            {
                _currentWord.Append(c);
                EvaluateWordSuggestion(fgHwnd);
            }
            else
            {
                ResetBuffer();
            }
        }
    }

    private void EvaluateWordSuggestion(nint fgHwnd)
    {
        string prefix = _currentWord.ToString();
        if (prefix.Length < Settings.MinPrefixLength)
        {
            HideHintImmediate();
            return;
        }

        string? match = null;
        foreach (var dict in _dictionaries)
        {
            match = dict.FindBestMatch(prefix);
            if (match != null) break;
        }

        if (string.IsNullOrEmpty(match) || match.Length <= prefix.Length)
        {
            HideHintImmediate();
            return;
        }

        // Calculate suffix preserving original case
        string suffix = match.Substring(prefix.Length);
        _currentSuggestionSuffix = suffix;

        // Obtain caret position
        var caretInfo = CaretPositionService.TryGetCaretPosition(fgHwnd);
        if (caretInfo == null)
        {
            HideHintImmediate();
            return;
        }

        // Display hint on UI thread
        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            lock (_hintLock)
            {
                if (_hintOverlay == null)
                {
                    _hintOverlay = new T9InlineHintOverlay();
                }

                _hintOverlay.ShowHint(
                    suffix, 
                    caretInfo.CaretScreenLocation, 
                    caretInfo.CaretHeight, 
                    caretInfo.IsDarkBackground, 
                    Settings.HintOpacity);
            }
        }));
    }

    private void AcceptCurrentSuggestion()
    {
        string? suffix = _currentSuggestionSuffix;
        if (string.IsNullOrEmpty(suffix)) return;

        if (Settings.AddSpaceAfterWord)
        {
            suffix += " ";
        }

        HideHintImmediate();
        ResetBuffer();

        // Inject suffix via SendInput
        TextInjectionService.SendText(suffix);
    }

    private void ResetBuffer()
    {
        _currentWord.Clear();
        HideHintImmediate();
    }

    private void HideHintImmediate()
    {
        _currentSuggestionSuffix = null;
        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            lock (_hintLock)
            {
                _hintOverlay?.HideHint();
            }
        }));
    }
}
