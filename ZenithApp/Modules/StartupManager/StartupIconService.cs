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
    private const uint SHGFI_SYSICONINDEX = 0x000004000;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const int SHIL_LARGE = 0;
    private const int SHIL_EXTRALARGE = 2;
    private const int ILD_TRANSPARENT = 0x00000001;
    private static readonly Guid IID_IImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(nint hbmImage, nint hbmMask, ref int pi);
        [PreserveSig] int ReplaceIcon(int i, nint hicon, ref int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, nint hbmImage, nint hbmMask);
        [PreserveSig] int AddMasked(nint hbmImage, int crMask, ref int pi);
        [PreserveSig] int Draw(nint pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, out nint picon);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

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
        // 1. Системный список иконок Shell без наложения стрелки ярлыка (.lnk)
        var cleanShellIcon = TryExtractCleanShellIcon(path);
        if (cleanShellIcon != null)
        {
            return cleanShellIcon;
        }

        // 2. Попытка через System.Drawing.Icon.ExtractAssociatedIcon (резервный вариант для .exe и .lnk)
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

        // 3. Попытка через Shell32 SHGetFileInfo
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

    private static ImageSource? TryExtractCleanShellIcon(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return null;
        }

        try
        {
            var shinfo = new SHFILEINFO();
            var res = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_SYSICONINDEX);
            if (res == nint.Zero || shinfo.iIcon < 0)
            {
                return null;
            }

            foreach (var listType in new[] { SHIL_EXTRALARGE, SHIL_LARGE })
            {
                var iid = IID_IImageList;
                if (SHGetImageList(listType, ref iid, out var imageList) != 0 || imageList == null)
                {
                    continue;
                }

                if (imageList.GetIcon(shinfo.iIcon, ILD_TRANSPARENT, out var hIcon) != 0 || hIcon == nint.Zero)
                {
                    continue;
                }

                try
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(
                        hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }
        catch
        {
            // При ошибке переходим к резервным способам извлечения иконки
        }

        return null;
    }
}
