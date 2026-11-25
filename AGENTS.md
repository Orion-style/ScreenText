# Правила проекта ScreenText (Zenith Utility)

После каждого внесённого изменения в проекте обязательно выполнять:

1. **Сборку и проверку компиляции**:
   - Выполнить компиляцию решения:
     ```powershell
     dotnet build ZenithApp.sln -c Release
     ```
   - Запустить тесты:
     ```powershell
     dotnet test ZenithApp.sln -c Release
     ```
   - Обязательно выполнить сборку приложения (`ZenithApp.exe`) после изменений кода приложения:
     ```powershell
     dotnet publish ZenithApp/ZenithApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o build_output
     ```
   - При необходимости сборки/обновления инсталлятора ориентироваться на сценарий `scripts/build-installer.ps1`.

2. **Коммит и отправку в GitHub**:
   - После успешной сборки компиляции и прохождения тестов зафиксировать изменения в Git и отправить их в удалённый репозиторий GitHub (`git add`, `git commit` с осмысленным сообщением, `git push`).
   - Если локальный репозиторий `.git`, удалённый `origin` или `git`/`gh` CLI в текущем окружении не настроены или недоступны — явно сообщить об этом пользователю и не пропускать шаг молча.
