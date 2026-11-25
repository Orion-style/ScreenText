using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZenithApp.Modules.QuickLook;

/// <summary>
/// Resilient multi-tier image loader for Quick Look preview.
/// Supports standard formats (PNG, JPG, BMP, GIF, ICO, TIFF) with EXIF orientation correction,
/// modern formats (WebP, HEIC, AVIF, RAW) via Shell Thumbnail API,
/// and GDI+ bitmap decoding fallback.
/// </summary>
public static class QuickLookImageLoader
{
    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(
            [In, MarshalAs(UnmanagedType.Struct)] SIZE size,
            [In] SIIGBF flags,
            [Out] out nint phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
        public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
    }

    [Flags]
    private enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00,
        SIIGBF_BIGGERSIZEOK = 0x01,
        SIIGBF_MEMORYONLY = 0x02,
        SIIGBF_ICONONLY = 0x04,
        SIIGBF_THUMBNAILONLY = 0x08,
        SIIGBF_INCACHEONLY = 0x10
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        nint pbc,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint hObject);

    private static readonly Guid IShellItemImageFactoryGuid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>
    /// Loads an image as a frozen WPF BitmapSource using multi-tier fallback decoders.
    /// Returns null if the file cannot be decoded by any method.
    /// </summary>
    public static BitmapSource? LoadImage(string filePath, int targetResolution = 2560)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        // 1. Try native WIC BitmapDecoder with EXIF orientation handling
        var wicResult = TryLoadWicWithExif(filePath);
        if (wicResult != null)
        {
            return wicResult;
        }

        // 2. Try Windows Shell Image Factory (handles WebP, HEIC, AVIF, RAW, etc.)
        var shellResult = TryLoadShellThumbnail(filePath, targetResolution);
        if (shellResult != null)
        {
            return shellResult;
        }

        // 3. Fallback to GDI+ System.Drawing.Bitmap
        var gdiResult = TryLoadGdi(filePath);
        if (gdiResult != null)
        {
            return gdiResult;
        }

        return null;
    }

    private static BitmapSource? TryLoadWicWithExif(string filePath)
    {
        try
        {
            byte[] fileBytes;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                fileBytes = ms.ToArray();
            }

            if (fileBytes.Length == 0) return null;

            using var memStream = new MemoryStream(fileBytes);
            var decoder = BitmapDecoder.Create(
                memStream,
                BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count > 0)
            {
                var frame = decoder.Frames[0];
                int rotationAngle = 0;

                try
                {
                    if (frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery("/app1/ifd/{ushort=274}"))
                    {
                        var queryVal = metadata.GetQuery("/app1/ifd/{ushort=274}");
                        if (queryVal is ushort orient)
                        {
                            rotationAngle = orient switch
                            {
                                3 => 180,
                                6 => 90,
                                8 => 270,
                                _ => 0
                            };
                        }
                    }
                }
                catch
                {
                    // Ignore metadata query issues
                }

                if (rotationAngle != 0)
                {
                    var transformed = new TransformedBitmap(frame, new RotateTransform(rotationAngle));
                    transformed.Freeze();
                    return transformed;
                }

                frame.Freeze();
                return frame;
            }
        }
        catch
        {
            // Fall through to shell/GDI decoders
        }

        return null;
    }

    private static BitmapSource? TryLoadShellThumbnail(string filePath, int targetResolution)
    {
        try
        {
            int hr = SHCreateItemFromParsingName(filePath, nint.Zero, IShellItemImageFactoryGuid, out object factoryObj);
            if (hr == 0 && factoryObj is IShellItemImageFactory factory)
            {
                int dimension = Math.Clamp(targetResolution, 800, 3840);
                hr = factory.GetImage(new SIZE(dimension, dimension), SIIGBF.SIIGBF_BIGGERSIZEOK, out nint hBitmap);
                if (hr == 0 && hBitmap != nint.Zero)
                {
                    try
                    {
                        var bmp = Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            nint.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        bmp.Freeze();
                        return bmp;
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
            }
        }
        catch
        {
            // Ignore shell thumbnail errors
        }

        return null;
    }

    private static BitmapSource? TryLoadGdi(string filePath)
    {
        try
        {
            using var bmp = new Bitmap(filePath);
            nint hBitmap = bmp.GetHbitmap();
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    nint.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch
        {
            return null;
        }
    }
}
