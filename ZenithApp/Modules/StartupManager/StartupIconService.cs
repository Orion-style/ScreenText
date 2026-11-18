using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZenithApp.Modules.StartupManager;

public static class StartupIconService
{
    private static readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

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
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);

    public static ImageSource? GetIcon(string? filePath, string? command)
    {
        var targetPath = ResolvePath(filePath, command);
        if (string.IsNullOrWhiteSpace(targetPath)) return null;

        if (_cache.TryGetValue(targetPath, out var cached))
        {
            return cached;
        }

        var icon = ExtractIcon(targetPath);
        _cache[targetPath] = icon;
        return icon;
    }

    private static string? ResolvePath(string? filePath, string? command)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && (File.Exists(filePath) || Directory.Exists(filePath)))
        {
            return filePath;
        }

        if (!string.IsNullOrWhiteSpace(command))
        {
            var extracted = StartupItemsService.ExtractExecutablePath(command);
            if (!string.IsNullOrWhiteSpace(extracted) && File.Exists(extracted))
            {
                return extracted;
            }
        }

        return filePath;
    }

    private static ImageSource? ExtractIcon(string path)
    {
        // 1. Попытка через System.Drawing.Icon.ExtractAssociatedIcon (наиболее надежно для .exe и .lnk)
        try
        {
            if (File.Exists(path))
            {
                using var sysIcon = Icon.ExtractAssociatedIcon(path);
                if (sysIcon != null)
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(
                        sysIcon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
            }
        }
        catch { }

        // 2. Попытка через Shell32 SHGetFileInfo
        try
        {
            var shinfo = new SHFILEINFO();
            var res = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);
            if (res != nint.Zero && shinfo.hIcon != nint.Zero)
            {
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(
                        shinfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally
                {
                    DestroyIcon(shinfo.hIcon);
                }
            }
        }
        catch { }

        return null;
    }
}
