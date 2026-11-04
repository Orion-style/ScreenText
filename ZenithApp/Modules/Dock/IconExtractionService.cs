using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZenithApp.Modules.Dock;

public static class IconExtractionService
{
    private static readonly ConcurrentDictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

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

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);

    /// <summary>
    /// Resolves target if path is a shortcut (.lnk), or returns original path.
    /// </summary>
    public static string ResolveShortcutTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType)!;
                    dynamic shortcut = shell.CreateShortcut(path);
                    string target = shortcut.TargetPath;
                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        return target;
                    }
                }
            }
            catch
            {
                // Fall back to original path if WScript fails
            }
        }
        return path;
    }

    /// <summary>
    /// Extracts cached ImageSource icon (max 64-96px) from file, exe, directory or link.
    /// Returns null if path is completely missing or icon cannot be extracted.
    /// </summary>
    public static ImageSource? GetIcon(string path, int maxDimension = 64)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var cacheKey = $"{path.ToLowerInvariant()}_{maxDimension}";
        if (_iconCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var icon = ExtractIconInternal(path, maxDimension);
        _iconCache[cacheKey] = icon;
        return icon;
    }

    private static ImageSource? ExtractIconInternal(string path, int maxDimension)
    {
        try
        {
            // If path does not exist, return null so UI can render fallback gray icon
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                // Check if resolving shortcut target works
                var resolved = ResolveShortcutTarget(path);
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    return null;
                }
            }

            var shinfo = new SHFILEINFO();
            var res = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);
            if (res != nint.Zero && shinfo.hIcon != nint.Zero)
            {
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(
                        shinfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(maxDimension, maxDimension));

                    source.Freeze();
                    return source;
                }
                finally
                {
                    DestroyIcon(shinfo.hIcon);
                }
            }

            // Fallback using System.Drawing.Icon.ExtractAssociatedIcon for .exe files
            if (File.Exists(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                using var associatedIcon = Icon.ExtractAssociatedIcon(path);
                if (associatedIcon != null)
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(
                        associatedIcon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(maxDimension, maxDimension));

                    source.Freeze();
                    return source;
                }
            }
        }
        catch
        {
            // Do not crash on corrupt shortcuts/executables
        }

        return null;
    }

    public static void ClearCache()
    {
        _iconCache.Clear();
    }
}
