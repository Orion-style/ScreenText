using System.Runtime.InteropServices;

namespace ZenithApp.Modules.Screenshot;

public static class ScreenCaptureService
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public static System.Drawing.Rectangle GetVirtualScreenBounds()
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        return new System.Drawing.Rectangle(x, y, width, height);
    }

    public static System.Drawing.Bitmap CaptureVirtualScreen()
    {
        var bounds = GetVirtualScreenBounds();
        var bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, System.Drawing.CopyPixelOperation.SourceCopy);
        }
        return bmp;
    }

    public static System.Drawing.Bitmap CropBitmap(System.Drawing.Bitmap source, System.Drawing.Rectangle cropArea)
    {
        // Ограничиваем cropArea границами source
        var srcRect = new System.Drawing.Rectangle(0, 0, source.Width, source.Height);
        cropArea.Intersect(srcRect);

        if (cropArea.Width <= 0 || cropArea.Height <= 0)
        {
            return new System.Drawing.Bitmap(1, 1);
        }

        var cropped = new System.Drawing.Bitmap(cropArea.Width, cropArea.Height, source.PixelFormat);
        using (var g = System.Drawing.Graphics.FromImage(cropped))
        {
            g.DrawImage(source, new System.Drawing.Rectangle(0, 0, cropArea.Width, cropArea.Height), cropArea, System.Drawing.GraphicsUnit.Pixel);
        }
        return cropped;
    }
}
