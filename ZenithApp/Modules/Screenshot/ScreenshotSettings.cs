using System.Windows.Input;
using ZenithApp.Core.Hotkeys;

namespace ZenithApp.Modules.Screenshot;

public sealed class ScreenshotSettings
{
    // Обычный скриншот: по умолчанию Ctrl + Shift + S
    public KeyModifiers ScreenshotModifiers { get; set; } = KeyModifiers.Control | KeyModifiers.Shift;
    public Key ScreenshotKey { get; set; } = Key.S;

    // Скриншот с OCR: по умолчанию Ctrl + Shift + T
    public KeyModifiers OcrModifiers { get; set; } = KeyModifiers.Control | KeyModifiers.Shift;
    public Key OcrKey { get; set; } = Key.T;

    // Дополнительное сохранение скриншота в папку
    public bool SaveToFileEnabled { get; set; } = false;
    public string SaveFolderPath { get; set; } = string.Empty;
}
