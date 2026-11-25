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

    // Двухшаговый режим: 1-й вызов — выбор области, 2-й вызов — мгновенный снимок выбранной области
    public bool TwoStepCaptureEnabled { get; set; } = true;

    // Создавать снимок уже при первом вызове (выборе)
    public bool CaptureOnFirstCall { get; set; } = true;

    // Автоматический сброс области после второго вызова (режим 2-шагового цикла: 1-й выбор -> 2-й снимок -> 3-й выбор)
    public bool TwoStepResetAfterCapture { get; set; } = true;

    // Лупа и пиксельная сетка (в стиле ShareX для точного выделения)
    public bool MagnifierEnabled { get; set; } = true;

    // Масштаб увеличения пикселей (по умолчанию 8x, от 2x до 24x)
    public int MagnifierZoom { get; set; } = 8;

    // Размер сетки (количество пикселей по ширине/высоте: например, 7, 9, 11, 13, 15)
    public int MagnifierGridSize { get; set; } = 11;

    // Отображение информации о цвете (HEX, RGB) и координатах
    public bool MagnifierShowColorInfo { get; set; } = true;
}
