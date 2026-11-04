## ZenithApp — Каркас лёгкой десктопной утилиты для Windows 10/11

Современная модульная база утилиты на C# / .NET 8 / WPF с поддержкой тем оформления Fluent (Mica/Acrylic), системного трея и изоляцией памяти выключенных модулей.

---

### Архитектура и структура папок

```
c:\Project\10\ZenithApp\
│
├── App.xaml / App.xaml.cs          # Точка входа WPF: DI-контейнер, Mutex, трей, управление жизненным циклом
├── ZenithApp.csproj                # Конфигурация сборки и публикации
├── app.manifest                    # Манифест PerMonitorV2 DPI и поддержки Windows 10/11
│
├── Core\                           # Системное ядро утилиты
│   ├── Hotkeys\                    # Сервис глобальных горячих клавиш (Win32 HWND_MESSAGE без окон)
│   │   └── HotkeyService.cs
│   ├── Lifecycle\                  # Защита от второго экземпляра (Named Mutex + Named Pipe activation)
│   │   └── SingleInstanceService.cs
│   ├── Logging\                    # Автономное легковесное логирование с ротацией файлов
│   │   ├── FileLogger.cs
│   │   └── IAppLogger.cs
│   ├── Modules\                    # Контракты и диспетчер модулей (ленивая загрузка, изоляция сбоев)
│   │   ├── BaseFeatureModule.cs
│   │   ├── IFeatureModule.cs
│   │   └── ModuleManager.cs
│   ├── Settings\                   # Хранилище настроек (%AppData%\ZenithApp\settings.json, атомарная запись)
│   │   ├── AppSettings.cs
│   │   └── SettingsService.cs
│   └── Startup\                    # Автозапуск через HKCU\Software\Microsoft\Windows\CurrentVersion\Run
│       └── AutoLaunchService.cs
│
├── Modules\                        # Папки модулей (изолированные плагины)
│   ├── Screenshot\                 # 1. Скриншот области и OCR
│   │   └── ScreenshotModule.cs
│   ├── SmartLinks\                 # 3. Умные подсказки для ссылок и QR
│   │   └── SmartLinksModule.cs
│   ├── QuickLook\                  # 4. Quick Look по клавише Space
│   │   └── QuickLookModule.cs
│   ├── SnapZones\                  # 5. Закрепление окон по зонам / Always on Top
│   │   └── SnapZonesModule.cs
│
├── Resources\                      # Ресурсы, строки локализации и иконки
│   ├── app.ico                     # Системная иконка
│   └── Strings.xaml                # Строковые ресурсы (русский язык по умолчанию)
│
└── UI\                             # Представления и модели представлений (MVVM)
    ├── Common\                     # Базовые классы RelayCommand и ViewModelBase
    │   ├── RelayCommand.cs
    │   └── ViewModelBase.cs
    ├── Tray\                       # Интеграция с треем (контекстное меню, double-click)
    │   └── TrayIconService.cs
    ├── ViewModels\                 # ViewModels для настроек и модулей
    │   ├── ModuleItemViewModel.cs
    │   └── SettingsViewModel.cs
    └── Views\                      # Окно настроек с эффектом Mica
        ├── SettingsWindow.xaml
        └── SettingsWindow.xaml.cs
```

---

### Как добавить новый модуль (пошагово)

1. В папке `Modules\` создайте новую папку (например, `Modules\ClipboardManager\`).
2. Создайте класс модуля, унаследованный от `BaseFeatureModule`:
   ```csharp
   public sealed class ClipboardModule : BaseFeatureModule
   {
       public const string ModuleId = "clipboard_manager";

       public ClipboardModule(IAppLogger logger) : base(logger) { }

       public override string Id => ModuleId;
       public override string Name => "Менеджер буфера обмена";
       public override string Description => "История буфера обмена с поиском.";

       protected override Task OnEnableAsync()
       {
           Logger.LogInfo("[Clipboard] Модуль активирован.");
           return Task.CompletedTask;
       }

       protected override Task OnDisableAsync()
       {
           Logger.LogInfo("[Clipboard] Ресурсы освобождены.");
           return Task.CompletedTask;
       }
   }
   ```
3. Зарегистрируйте модуль в одном месте — в методе `ConfigureServices` в `App.xaml.cs`:
   ```csharp
   services.AddSingleton<IFeatureModule, ClipboardModule>();
   ```
4. Всё: модуль автоматически появится в списке окна настроек, его состояние будет сохраняться в `%AppData%\ZenithApp\settings.json`, а включение и выключение будет изолированно управляться через `ModuleManager`.
