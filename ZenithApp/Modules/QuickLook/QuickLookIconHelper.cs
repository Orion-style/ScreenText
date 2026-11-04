using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZenithApp.Modules.QuickLook;

public static class QuickLookIconHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);

    public static ImageSource? GetFileIcon(string filePath, int dimension = 64)
    {
        try
        {
            var shinfo = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_LARGEICON;
            if (!File.Exists(filePath) && !Directory.Exists(filePath))
            {
                flags |= SHGFI_USEFILEATTRIBUTES;
            }

            var res = SHGetFileInfo(filePath, FILE_ATTRIBUTE_NORMAL, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
            if (res != nint.Zero && shinfo.hIcon != nint.Zero)
            {
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(
                        shinfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(dimension, dimension));
                    source.Freeze();
                    return source;
                }
                finally
                {
                    DestroyIcon(shinfo.hIcon);
                }
            }
        }
        catch
        {
            // Ignore icon failure
        }

        try
        {
            using var sysIcon = new Icon(SystemIcons.Application, dimension, dimension);
            var src = Imaging.CreateBitmapSourceFromHIcon(
                sysIcon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(dimension, dimension));
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
    }
}
