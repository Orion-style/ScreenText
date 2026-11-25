namespace ZenithApp.Modules.QuickLook;

/// <summary>
/// Pure layout and scaling calculations for Quick Look windows, images, and PDF pages.
/// </summary>
public static class QuickLookLayoutHelper
{
    public readonly record struct TargetWindowBounds(double Width, double Height, double MaxWidth, double MaxHeight);
    public readonly record struct DisplaySize(double Width, double Height, bool NeedsScroll);

    public static TargetWindowBounds ComputeTargetWindowBounds(
        double contentWidth,
        double contentHeight,
        double workAreaWidth,
        double workAreaHeight,
        int maxWindowScreenPercent,
        double extraWidth = 34,
        double extraHeight = 76)
    {
        double percent = Math.Clamp(maxWindowScreenPercent, 40, 95) / 100.0;
        double safeWorkW = Math.Max(1, workAreaWidth);
        double safeWorkH = Math.Max(1, workAreaHeight);

        double maxW = Math.Clamp(safeWorkW * percent, Math.Min(480, safeWorkW), safeWorkW);
        double maxH = Math.Clamp(safeWorkH * percent, Math.Min(340, safeWorkH), safeWorkH);

        double minW = Math.Min(520, maxW);
        double minH = Math.Min(380, maxH);

        if (contentWidth <= 0 || contentHeight <= 0)
        {
            return new TargetWindowBounds(
                Math.Clamp(940, minW, maxW),
                Math.Clamp(680, minH, maxH),
                maxW,
                maxH);
        }

        double maxContentW = Math.Max(120, maxW - extraWidth);
        double maxContentH = Math.Max(120, maxH - extraHeight);

        double scale = Math.Min(1.0, Math.Min(maxContentW / contentWidth, maxContentH / contentHeight));
        double targetW = Math.Clamp(contentWidth * scale + extraWidth, minW, maxW);
        double targetH = Math.Clamp(contentHeight * scale + extraHeight, minH, maxH);

        return new TargetWindowBounds(targetW, targetH, maxW, maxH);
    }

    public static DisplaySize ComputeImageDisplaySize(
        double imageWidth,
        double imageHeight,
        double viewportWidth,
        double viewportHeight,
        double zoomFactor)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return new DisplaySize(0, 0, false);
        }

        double clampedZoom = Math.Clamp(zoomFactor, 0.2, 5.0);
        double fitScale = Math.Min(1.0, Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight));
        double baseW = imageWidth * fitScale;
        double baseH = imageHeight * fitScale;

        double displayW = Math.Max(1.0, baseW * clampedZoom);
        double displayH = Math.Max(1.0, baseH * clampedZoom);
        bool needsScroll = displayW > viewportWidth + 1.0 || displayH > viewportHeight + 1.0;

        return new DisplaySize(displayW, displayH, needsScroll);
    }

    public static DisplaySize ComputePdfPageDisplaySize(
        double pagePixelWidth,
        double pagePixelHeight,
        double availableWidth,
        double zoomFactor)
    {
        if (pagePixelWidth <= 0 || pagePixelHeight <= 0 || availableWidth <= 0)
        {
            return new DisplaySize(0, 0, false);
        }

        double clampedZoom = Math.Clamp(zoomFactor, 0.5, 3.0);
        double baseW = availableWidth;
        double baseH = pagePixelHeight * (availableWidth / pagePixelWidth);

        double displayW = Math.Max(1.0, baseW * clampedZoom);
        double displayH = Math.Max(1.0, baseH * clampedZoom);
        bool needsHorizontalScroll = displayW > availableWidth + 1.0;

        return new DisplaySize(displayW, displayH, needsHorizontalScroll);
    }

    public static uint ComputePdfRenderDestinationWidth(
        double pageWidth,
        double pageHeight,
        uint maxWidth = 1800,
        uint maxHeight = 2600)
    {
        if (pageWidth <= 0 || pageHeight <= 0)
        {
            return maxWidth;
        }

        double maxScale = Math.Min(maxWidth / pageWidth, maxHeight / pageHeight);
        double desiredScale = Math.Max(1.0, Math.Min(2.0, 1400.0 / pageWidth));
        double scale = Math.Min(desiredScale, maxScale);

        return Math.Max(1u, (uint)Math.Round(pageWidth * scale));
    }
}
