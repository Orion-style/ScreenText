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

    // Сохранение последней выбранной области: на следующих вызовах появляется в той же форме и позиции с возможностью перемещения без изменения пропорций
    public bool RememberLastArea { get; set; } = true;

    // Круглая лупа с пиксельной сеткой для точного выделения
    public bool MagnifierEnabled { get; set; } = true;

    // Масштаб увеличения пикселей (по умолчанию 8x, от 2x до 24x)
    public int MagnifierZoom { get; set; } = 8;

    // Размер сетки (количество пикселей по ширине/высоте: например, 7, 9, 11, 13, 15)
    public int MagnifierGridSize { get; set; } = 11;
}
