using System.Windows;
using System.Windows.Media;

namespace ZenithApp.Modules.StickyNotes;

public sealed record StickyNoteTheme(
    string Name,
    string BackgroundHex,
    string HeaderHex,
    string BorderHex,
    string ForegroundHex,
    string MutedForegroundHex);

public static class StickyNotePalette
{
    public static IReadOnlyList<StickyNoteTheme> Presets { get; } =
    [
        new("Графит", "#232730", "#2C313C", "#3F4654", "#F3F4F6", "#9CA3AF"),
        new("Янтарь", "#332B18", "#42371E", "#6B582C", "#FEF3C7", "#D6C08A"),
        new("Сапфир", "#1B2838", "#23344A", "#385375", "#E0F2FE", "#93B7D8"),
        new("Изумруд", "#192E27", "#203D33", "#326150", "#D1FAE5", "#8AC4AE"),
        new("Вишня", "#331D24", "#442530", "#6B3849", "#FCE7F3", "#D19DB2"),
        new("Светлый", "#FFF8DC", "#F5E9B8", "#D9C98E", "#1F2937", "#6B7280")
    ];

    public static StickyNoteTheme Resolve(string? backgroundHex)
    {
        if (!string.IsNullOrWhiteSpace(backgroundHex))
        {
            foreach (var preset in Presets)
            {
                if (string.Equals(preset.BackgroundHex, backgroundHex, StringComparison.OrdinalIgnoreCase))
                {
                    return preset;
                }
            }
        }

        return Presets[0];
    }

    public static SolidColorBrush CreateFrozenBrush(string hex, byte alpha = 255)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        if (alpha < 255)
        {
            color = Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public sealed class StickyNoteItem
{
    public const double MinWidth = 200;
    public const double MinHeight = 120;
    public const double DefaultWidth = 280;
    public const double DefaultHeight = 220;
    public const double HeaderHeight = 30;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Content { get; set; } = string.Empty;
    public double Left { get; set; } = 120;
    public double Top { get; set; } = 120;
    public double Width { get; set; } = DefaultWidth;
    public double Height { get; set; } = DefaultHeight;
    public string ColorHex { get; set; } = "#232730";
    public double Opacity { get; set; } = 0.92;
    public double FontSize { get; set; } = 14;
    public bool IsPinnedTopmost { get; set; } = true;
    public bool IsClickThrough { get; set; } = false;
    public bool IsCollapsed { get; set; } = false;
    public bool IsVisible { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public StickyNoteItem Clone() => new()
    {
        Id = Id,
        Content = Content,
        Left = Left,
        Top = Top,
        Width = Width,
        Height = Height,
        ColorHex = ColorHex,
        Opacity = Opacity,
        FontSize = FontSize,
        IsPinnedTopmost = IsPinnedTopmost,
        IsClickThrough = IsClickThrough,
        IsCollapsed = IsCollapsed,
        IsVisible = IsVisible,
        CreatedAtUtc = CreatedAtUtc,
        UpdatedAtUtc = UpdatedAtUtc
    };
}

public static class StickyNoteBoundsHelper
{
    public static (double Left, double Top, double Width, double Height) ClampToVirtualScreen(
        double left,
        double top,
        double width,
        double height,
        Rect virtualScreen)
    {
        var clampedWidth = double.IsFinite(width)
            ? Math.Clamp(width, StickyNoteItem.MinWidth, Math.Max(StickyNoteItem.MinWidth, virtualScreen.Width))
            : StickyNoteItem.DefaultWidth;

        var clampedHeight = double.IsFinite(height)
            ? Math.Clamp(height, StickyNoteItem.MinHeight, Math.Max(StickyNoteItem.MinHeight, virtualScreen.Height))
            : StickyNoteItem.DefaultHeight;

        if (virtualScreen.Width <= 0 || virtualScreen.Height <= 0)
        {
            return (120, 120, clampedWidth, clampedHeight);
        }

        const double visibleMarginX = 64;
        const double visibleMarginY = StickyNoteItem.HeaderHeight;

        var minLeft = virtualScreen.Left - clampedWidth + visibleMarginX;
        var maxLeft = virtualScreen.Right - visibleMarginX;
        var minTop = virtualScreen.Top;
        var maxTop = virtualScreen.Bottom - visibleMarginY;

        if (maxLeft < minLeft) maxLeft = minLeft;
        if (maxTop < minTop) maxTop = minTop;

        var safeLeft = double.IsFinite(left) ? Math.Clamp(left, minLeft, maxLeft) : virtualScreen.Left + 120;
        var safeTop = double.IsFinite(top) ? Math.Clamp(top, minTop, maxTop) : virtualScreen.Top + 120;

        return (safeLeft, safeTop, clampedWidth, clampedHeight);
    }

    public static Rect GetCurrentVirtualScreen() => new(
        SystemParameters.VirtualScreenLeft,
        SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth,
        SystemParameters.VirtualScreenHeight);
}
