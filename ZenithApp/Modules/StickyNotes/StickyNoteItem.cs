using System.Windows;
using System.Windows.Media;

namespace ZenithApp.Modules.StickyNotes;

public sealed record StickyNoteTheme(
    string Id,
    string Name,
    IReadOnlyList<string> GradientStops,
    string BorderHex,
    string BorderEndHex,
    string ForegroundHex,
    string MutedForegroundHex)
{
    public string BackgroundHex => GradientStops[0];
    public bool IsGradient => GradientStops.Count > 1;
}

public static class StickyNotePalette
{
    public static IReadOnlyList<StickyNoteTheme> Presets { get; } =
    [
        // Базовые спокойные темы
        new("graphite", "Графит", ["#232730"], "#3F4654", "#3F4654", "#F3F4F6", "#9CA3AF"),
        new("amber", "Янтарь", ["#332B18"], "#6B582C", "#6B582C", "#FEF3C7", "#D6C08A"),
        new("light", "Светлый", ["#FFF8DC"], "#D9C98E", "#D9C98E", "#1F2937", "#6B7280"),

        // Переливающиеся темы в стиле Discord
        new("discord_blurple", "Discord Blurple", ["#262B6E", "#464EC4", "#7042A8", "#943584"], "#6873F5", "#C45CB3", "#F8FAFC", "#CBD5E1"),
        new("discord_aurora", "Северное сияние", ["#0F4C5C", "#1A6B64", "#3B429F", "#702963"], "#2BB3A3", "#A44CD3", "#F0FDFA", "#B8E6DF"),
        new("discord_sunset", "Закат", ["#6E2346", "#9A3947", "#652E6E", "#28316B"], "#D45C73", "#6472D6", "#FFF1F2", "#E2BAC4"),
        new("discord_cyber", "Неоновая мята", ["#114B47", "#155D7A", "#1E3A8A", "#4C1D95"], "#2DD4BF", "#8B5CF6", "#ECFEFF", "#A5D8DD"),
        new("discord_chroma", "Хрома-призма", ["#1E3A5F", "#5B2C6F", "#873566", "#2B5B6C"], "#7C5CFF", "#E06C9F", "#F8FAFC", "#CBD5E1"),
        new("discord_cotton", "Пастель", ["#EAD6F7", "#F9DFEC", "#DBEBFB", "#D6F5EE"], "#C29CE8", "#8DC8E8", "#1F2937", "#526075")
    ];

    public static StickyNoteTheme Resolve(string? idOrHex)
    {
        if (!string.IsNullOrWhiteSpace(idOrHex))
        {
            foreach (var preset in Presets)
            {
                if (string.Equals(preset.Id, idOrHex, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(preset.BackgroundHex, idOrHex, StringComparison.OrdinalIgnoreCase))
                {
                    return preset;
                }
            }

            // Обратная совместимость со старыми однотонными hex-пресетами
            if (string.Equals(idOrHex, "#1B2838", StringComparison.OrdinalIgnoreCase)) return Presets[3];
            if (string.Equals(idOrHex, "#192E27", StringComparison.OrdinalIgnoreCase)) return Presets[4];
            if (string.Equals(idOrHex, "#331D24", StringComparison.OrdinalIgnoreCase)) return Presets[5];
        }

        return Presets[0];
    }

    public static SolidColorBrush CreateFrozenBrush(string hex, byte alpha = 255)
    {
        var color = ParseColorWithAlpha(hex, alpha);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static Brush CreateBackgroundBrush(StickyNoteTheme theme, double opacity)
    {
        byte alpha = (byte)Math.Clamp((int)Math.Round(opacity * 255), 89, 255);
        if (theme.GradientStops.Count <= 1)
        {
            return CreateFrozenBrush(theme.GradientStops[0], alpha);
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };

        int count = theme.GradientStops.Count;
        for (int i = 0; i < count; i++)
        {
            var color = ParseColorWithAlpha(theme.GradientStops[i], alpha);
            brush.GradientStops.Add(new GradientStop(color, (double)i / (count - 1)));
        }

        brush.Freeze();
        return brush;
    }

    public static Brush CreateHeaderOverlayBrush(StickyNoteTheme theme, double opacity)
    {
        byte alpha = (byte)Math.Clamp((int)Math.Round(opacity * 255), 89, 255);
        if (theme.GradientStops.Count <= 1)
        {
            return CreateFrozenBrush(theme.GradientStops[0], alpha);
        }

        // Верхняя кромка градиента плавно переходит слева направо без отдельной границы с телом стикера
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        brush.GradientStops.Add(new GradientStop(ParseColorWithAlpha(theme.GradientStops[0], alpha), 0.0));
        brush.GradientStops.Add(new GradientStop(ParseColorWithAlpha(theme.GradientStops[1], alpha), 1.0));
        brush.Freeze();
        return brush;
    }

    public static Brush CreateBorderBrush(StickyNoteTheme theme)
    {
        if (string.Equals(theme.BorderHex, theme.BorderEndHex, StringComparison.OrdinalIgnoreCase))
        {
            return CreateFrozenBrush(theme.BorderHex);
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(theme.BorderHex), 0.0));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(theme.BorderEndHex), 1.0));
        brush.Freeze();
        return brush;
    }

    public static Brush CreatePopupBackgroundBrush(StickyNoteTheme theme)
    {
        // Для всплывающего меню настроек используем плотный, хорошо читаемый фон в тон темы
        return CreateBackgroundBrush(theme, 0.98);
    }

    private static Color ParseColorWithAlpha(string hex, byte alpha)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        return alpha < 255
            ? Color.FromArgb(alpha, color.R, color.G, color.B)
            : color;
    }
}

public sealed class StickyNoteItem
{
    public const double MinWidth = 200;
    public const double MinHeight = 64;
    public const double DefaultWidth = 270;
    public const double DefaultHeight = 84;
    public const double HeaderHeight = 24;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Content { get; set; } = string.Empty;
    public double Left { get; set; } = 120;
    public double Top { get; set; } = 120;
    public double Width { get; set; } = DefaultWidth;
    public double Height { get; set; } = DefaultHeight;
    public bool AutoHeight { get; set; } = true;
    public string ColorHex { get; set; } = "graphite";
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
        AutoHeight = AutoHeight,
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
