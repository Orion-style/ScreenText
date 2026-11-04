using System.Windows.Input;

namespace ZenithApp.Modules.KeyRemap;

public static class VirtualKeyHelper
{
    public const int VK_LBUTTON = 0x01;
    public const int VK_CANCEL = 0x03;
    public const int VK_BACK = 0x08;
    public const int VK_TAB = 0x09;
    public const int VK_CLEAR = 0x0C;
    public const int VK_RETURN = 0x0D;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12; // Alt
    public const int VK_PAUSE = 0x13;
    public const int VK_CAPITAL = 0x14; // CapsLock
    public const int VK_ESCAPE = 0x1B;
    public const int VK_SPACE = 0x20;
    public const int VK_PRIOR = 0x21; // PageUp
    public const int VK_NEXT = 0x22;  // PageDown
    public const int VK_END = 0x23;
    public const int VK_HOME = 0x24;
    public const int VK_LEFT = 0x25;
    public const int VK_UP = 0x26;
    public const int VK_RIGHT = 0x27;
    public const int VK_DOWN = 0x28;
    public const int VK_SNAPSHOT = 0x2C; // PrintScreen
    public const int VK_INSERT = 0x2D;
    public const int VK_DELETE = 0x2E;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    public const int VK_APPS = 0x5D;

    public const int VK_LSHIFT = 0xA0;
    public const int VK_RSHIFT = 0xA1;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_LMENU = 0xA4;
    public const int VK_RMENU = 0xA5;

    public static string GetKeyName(int vk)
    {
        if (vk == 0) return "Не назначена";

        return vk switch
        {
            VK_CAPITAL => "CapsLock",
            VK_BACK => "Backspace",
            VK_TAB => "Tab",
            VK_RETURN => "Enter",
            VK_ESCAPE => "Esc",
            VK_SPACE => "Space",
            VK_PRIOR => "PageUp",
            VK_NEXT => "PageDown",
            VK_END => "End",
            VK_HOME => "Home",
            VK_LEFT => "Left Arrow",
            VK_UP => "Up Arrow",
            VK_RIGHT => "Right Arrow",
            VK_DOWN => "Down Arrow",
            VK_SNAPSHOT => "PrintScreen",
            VK_INSERT => "Insert",
            VK_DELETE => "Delete",
            VK_LWIN or VK_RWIN => "Win",
            VK_APPS => "Menu",
            VK_LCONTROL or VK_RCONTROL or VK_CONTROL => "Ctrl",
            VK_LSHIFT or VK_RSHIFT or VK_SHIFT => "Shift",
            VK_LMENU or VK_RMENU or VK_MENU => "Alt",
            >= 0x70 and <= 0x87 => $"F{vk - 0x70 + 1}", // F1..F24
            _ => KeyInterop.KeyFromVirtualKey(vk) is Key k and not Key.None ? k.ToString() : $"VK 0x{vk:X2}"
        };
    }

    public static string FormatKeyCombination(int vk, bool ctrl, bool alt, bool shift, bool win)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");

        var keyName = GetKeyName(vk);
        // Avoid duplicating modifier name if the key itself is that modifier
        if (!parts.Contains(keyName) && vk != 0)
        {
            parts.Add(keyName);
        }

        return parts.Count > 0 ? string.Join(" + ", parts) : "Не назначено";
    }

    public static bool IsModifierKey(int vk)
    {
        return vk is VK_CONTROL or VK_LCONTROL or VK_RCONTROL
                  or VK_MENU or VK_LMENU or VK_RMENU
                  or VK_SHIFT or VK_LSHIFT or VK_RSHIFT
                  or VK_LWIN or VK_RWIN;
    }
}
