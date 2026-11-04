using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZenithApp.Core.Logging;

namespace ZenithApp.Modules.Dock;

public static class IconExtractionService
{
    private static readonly ConcurrentDictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? _defaultFallbackIcon;
    private static readonly object _fallbackLock = new();

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
    /// Extracts cached ImageSource icon from file, exe, directory or link.
    /// Returns fallback icon if extraction fails and logs the reason.
    /// </summary>
    public static ImageSource? GetIcon(string path, int maxDimension = 64, IAppLogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            logger?.LogWarning("[IconExtraction] Передан пустой путь к элементу дока.");
            return GetFallbackIcon(maxDimension);
        }

        var cacheKey = $"{path.ToLowerInvariant()}_{maxDimension}";
        if (_iconCache.TryGetValue(cacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        var icon = ExtractIconInternal(path, maxDimension, logger);
        if (icon == null)
        {
            icon = GetFallbackIcon(maxDimension);
        }

        if (icon != null)
        {
            _iconCache[cacheKey] = icon;
        }

        return icon;
    }

    private static ImageSource? ExtractIconInternal(string path, int maxDimension, IAppLogger? logger)
    {
        try
        {
            // 1. Для ярлыков .lnk пробуем извлечь значок непосредственно из самого .lnk через SHGetFileInfo
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                var lnkIcon = ExtractViaShGetFileInfo(path, 0, 0, maxDimension);
                if (lnkIcon != null)
                {
                    return lnkIcon;
                }

                // Если из ярлыка не вышло, разрешаем цель ярлыка
                var target = ResolveShortcutTarget(path);
                if (!string.IsNullOrWhiteSpace(target) && !target.Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    var targetIcon = ExtractIconInternal(target, maxDimension, logger);
                    if (targetIcon != null)
                    {
                        return targetIcon;
                    }
                }
            }

            // 2. Обычный файл или каталог
            if (File.Exists(path) || Directory.Exists(path))
            {
                var icon = ExtractViaShGetFileInfo(path, 0, 0, maxDimension);
                if (icon != null) return icon;

                // Для .exe дополнительно пробуем ExtractAssociatedIcon
                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    try
                    {
                        using var assoc = Icon.ExtractAssociatedIcon(path);
                        if (assoc != null)
                        {
                            var bmp = Imaging.CreateBitmapSourceFromHIcon(
                                assoc.Handle,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromWidthAndHeight(maxDimension, maxDimension));
                            bmp.Freeze();
                            return bmp;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning($"[IconExtraction] Ошибка ExtractAssociatedIcon для '{path}': {ex.Message}");
                    }
                }
            }
            else
            {
                // Путь не существует физически (например, файл перемещён или удалён)
                logger?.LogWarning($"[IconExtraction] Файл или каталог не найден: '{path}'. Используется синтетический значок по расширению/типу.");
                var synthetic = ExtractViaShGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, SHGFI_USEFILEATTRIBUTES, maxDimension);
                if (synthetic != null) return synthetic;
            }
        }
        catch (Exception ex)
        {
            logger?.LogError($"[IconExtraction] Исключение при извлечении значка для '{path}': {ex.Message}", ex);
        }

        logger?.LogWarning($"[IconExtraction] Не удалось извлечь значок для '{path}'. Будет использован запасной системный значок.");
        return null;
    }

    private static ImageSource? ExtractViaShGetFileInfo(string path, uint dwFileAttributes, uint extraFlags, int maxDimension)
    {
        var shinfo = new SHFILEINFO();
        var flags = SHGFI_ICON | SHGFI_LARGEICON | extraFlags;
        var res = SHGetFileInfo(path, dwFileAttributes, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
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

        return null;
    }

    /// <summary>
    /// Возвращает запасной системный значок общего приложения/файла.
    /// </summary>
    public static ImageSource? GetFallbackIcon(int maxDimension = 64)
    {
        lock (_fallbackLock)
        {
            if (_defaultFallbackIcon != null)
            {
                return _defaultFallbackIcon;
            }

            try
            {
                // Попытка 1: Значок приложения Zenith из ресурсов
                var baseDir = AppContext.BaseDirectory;
                var icoPath = Path.Combine(baseDir, "Resources", "app.ico");
                if (File.Exists(icoPath))
                {
                    using var ico = new Icon(icoPath, maxDimension, maxDimension);
                    var src = Imaging.CreateBitmapSourceFromHIcon(
                        ico.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(maxDimension, maxDimension));
                    src.Freeze();
                    _defaultFallbackIcon = src;
                    return _defaultFallbackIcon;
                }
            }
            catch
            {
                // Игнорируем
            }

            try
            {
                // Попытка 2: Системный значок приложения через SHGFI_USEFILEATTRIBUTES
                var shinfo = new SHFILEINFO();
                var res = SHGetFileInfo(".exe", FILE_ATTRIBUTE_NORMAL, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);
                if (res != nint.Zero && shinfo.hIcon != nint.Zero)
                {
                    try
                    {
                        var src = Imaging.CreateBitmapSourceFromHIcon(
                            shinfo.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromWidthAndHeight(maxDimension, maxDimension));
                        src.Freeze();
                        _defaultFallbackIcon = src;
                        return _defaultFallbackIcon;
                    }
                    finally
                    {
                        DestroyIcon(shinfo.hIcon);
                    }
                }
            }
            catch
            {
                // Игнорируем
            }

            try
            {
                // Попытка 3: SystemIcons.Application
                using var sysIcon = new Icon(SystemIcons.Application, maxDimension, maxDimension);
                var src = Imaging.CreateBitmapSourceFromHIcon(
                    sysIcon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(maxDimension, maxDimension));
                src.Freeze();
                _defaultFallbackIcon = src;
                return _defaultFallbackIcon;
            }
            catch
            {
                return null;
            }
        }
    }

    public static void ClearCache()
    {
        _iconCache.Clear();
    }
}
