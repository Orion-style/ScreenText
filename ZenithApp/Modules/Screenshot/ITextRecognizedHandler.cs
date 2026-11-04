using System.Drawing;

namespace ZenithApp.Modules.Screenshot;

public readonly record struct TextRecognizedEventArgs(
    string Text,
    Bitmap? Image,
    Rectangle SelectionBoundsScreen
);

public interface ITextRecognizedHandler
{
    /// <summary>
    /// Возвращает true, если обработчик полностью обработал результат (например, показал свою подсказку),
    /// и стандартный тост «Текст скопирован» показывать не требуется.
    /// </summary>
    Task<bool> OnTextRecognizedAsync(TextRecognizedEventArgs args);
}
