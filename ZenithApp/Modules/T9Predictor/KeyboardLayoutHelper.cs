using System.Runtime.InteropServices;

namespace ZenithApp.Modules.T9Predictor;

public static class KeyboardLayoutHelper
{
    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern nint GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ToUnicodeEx(
        uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszBuff,
        int cchBuff, uint wFlags, nint dwhkl);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    public static string GetCurrentInputLanguage(nint hkl)
    {
        // Lower word contains Primary Language Identifier
        int langId = (int)((long)hkl & 0xFFFF);
        int primaryLangId = langId & 0x3FF;

        return primaryLangId switch
        {
            0x19 => "ru", // Russian
            0x09 => "en", // English
            _ => "en"
        };
    }

    public static char? TranslateKeyToChar(uint virtualKey, uint scanCode, nint hkl)
    {
        byte[] keyboardState = new byte[256];
        if (!GetKeyboardState(keyboardState))
        {
            return null;
        }

        var sb = new System.Text.StringBuilder(8);
        int result = ToUnicodeEx(virtualKey, scanCode, keyboardState, sb, sb.Capacity, 0, hkl);

        if (result == 1 && sb.Length > 0)
        {
            return sb[0];
        }

        return null;
    }

    public static bool AreModifiersPressed()
    {
        const int VK_CONTROL = 0x11;
        const int VK_MENU = 0x12; // Alt
        const int VK_LWIN = 0x5B;
        const int VK_RWIN = 0x5C;

        return (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 ||
               (GetAsyncKeyState(VK_MENU) & 0x8000) != 0 ||
               (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 ||
               (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
    }
}
