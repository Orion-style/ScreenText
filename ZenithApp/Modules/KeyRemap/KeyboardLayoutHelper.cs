using System.Globalization;
using System.Runtime.InteropServices;

namespace ZenithApp.Modules.KeyRemap;

public readonly record struct KeyboardLayoutInfo(
    nint Handle,
    int LanguageId,
    string ShortCode,
    string DisplayName);

public static class KeyboardLayoutHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint idThread);

    /// <summary>
    /// Returns the active HKL keyboard layout handle for the foreground focused window/thread.
    /// </summary>
    public static nint GetForegroundLayoutHandle()
    {
        var targetHwnd = nint.Zero;

        var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (GetGUIThreadInfo(0, ref gti) && gti.hwndFocus != nint.Zero)
        {
            targetHwnd = gti.hwndFocus;
        }
        else
        {
            targetHwnd = GetForegroundWindow();
        }

        if (targetHwnd != nint.Zero)
        {
            var threadId = GetWindowThreadProcessId(targetHwnd, out _);
            if (threadId != 0)
            {
                var hkl = GetKeyboardLayout(threadId);
                if (hkl != nint.Zero)
                {
                    return hkl;
                }
            }
        }

        return GetKeyboardLayout(0);
    }

    /// <summary>
    /// Extracts language code and human-readable language name from a Windows HKL handle.
    /// </summary>
    public static KeyboardLayoutInfo GetLayoutInfo(nint hkl)
    {
        int langId = unchecked((int)((long)hkl & 0xFFFF));
        return GetLayoutInfoFromLanguageId(hkl, langId);
    }

    internal static KeyboardLayoutInfo GetLayoutInfoFromLanguageId(nint hkl, int langId)
    {
        if (langId <= 0)
        {
            return new KeyboardLayoutInfo(hkl, langId, "?", "Язык изменён");
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(langId);
            var shortCode = culture.TwoLetterISOLanguageName.ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(shortCode) || shortCode == "IV")
            {
                shortCode = !string.IsNullOrWhiteSpace(culture.ThreeLetterWindowsLanguageName)
                    ? culture.ThreeLetterWindowsLanguageName.ToUpperInvariant()
                    : $"0x{langId:X4}";
            }

            var nameCulture = (!culture.IsNeutralCulture &&
                               culture.Parent != null &&
                               culture.Parent.LCID != CultureInfo.InvariantCulture.LCID)
                ? culture.Parent
                : culture;

            var rawName = nameCulture.NativeName;
            if (string.IsNullOrWhiteSpace(rawName))
            {
                rawName = culture.DisplayName;
            }

            var displayName = CapitalizeFirst(rawName, nameCulture);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = shortCode;
            }

            return new KeyboardLayoutInfo(hkl, langId, shortCode, displayName);
        }
        catch (CultureNotFoundException)
        {
            return new KeyboardLayoutInfo(hkl, langId, $"0x{langId:X2}", "Язык изменён");
        }
    }

    private static string CapitalizeFirst(string value, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (trimmed.Length == 1)
            return char.ToUpper(trimmed[0], culture).ToString();

        return char.ToUpper(trimmed[0], culture) + trimmed[1..];
    }
}
