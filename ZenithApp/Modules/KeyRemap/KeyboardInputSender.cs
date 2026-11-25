using System.Runtime.InteropServices;

namespace ZenithApp.Modules.KeyRemap;

public static class KeyboardInputSender
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEKEYBDHARDWAREINPUT mkhi;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct MOUSEKEYBDHARDWAREINPUT
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    internal static int InputStructSize => Marshal.SizeOf<INPUT>();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static Action? CustomLanguageSwitchAction { get; set; }

    public static void SendAltShiftLanguageSwitch()
    {
        if (CustomLanguageSwitchAction != null)
        {
            CustomLanguageSwitchAction.Invoke();
            return;
        }

        // Release physical Ctrl or Win if held so they don't corrupt the Alt+Shift layout toggle
        var ctrlHeld = (GetAsyncKeyState(VirtualKeyHelper.VK_CONTROL) & 0x8000) != 0;
        var winHeld = ((GetAsyncKeyState(VirtualKeyHelper.VK_LWIN) & 0x8000) != 0) || ((GetAsyncKeyState(VirtualKeyHelper.VK_RWIN) & 0x8000) != 0);

        if (ctrlHeld) SendKeyUp(VirtualKeyHelper.VK_CONTROL);
        if (winHeld) SendKeyUp(VirtualKeyHelper.VK_LWIN);

        var inputs = new INPUT[4];

        // 1. Left Alt down
        inputs[0] = new INPUT
        {
            type = INPUT_KEYBOARD,
            mkhi = new MOUSEKEYBDHARDWAREINPUT
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)VirtualKeyHelper.VK_LMENU,
                    wScan = 0x38,
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = ZenithApp.Core.Input.KeyboardHookService.ExtraInfoAppMarker
                }
            }
        };

        // 2. Left Shift down
        inputs[1] = new INPUT
        {
            type = INPUT_KEYBOARD,
            mkhi = new MOUSEKEYBDHARDWAREINPUT
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)VirtualKeyHelper.VK_LSHIFT,
                    wScan = 0x2A,
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = ZenithApp.Core.Input.KeyboardHookService.ExtraInfoAppMarker
                }
            }
        };

        // 3. Left Shift up
        inputs[2] = new INPUT
        {
            type = INPUT_KEYBOARD,
            mkhi = new MOUSEKEYBDHARDWAREINPUT
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)VirtualKeyHelper.VK_LSHIFT,
                    wScan = 0x2A,
                    dwFlags = KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = ZenithApp.Core.Input.KeyboardHookService.ExtraInfoAppMarker
                }
            }
        };

        // 4. Left Alt up
        inputs[3] = new INPUT
        {
            type = INPUT_KEYBOARD,
            mkhi = new MOUSEKEYBDHARDWAREINPUT
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)VirtualKeyHelper.VK_LMENU,
                    wScan = 0x38,
                    dwFlags = KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = ZenithApp.Core.Input.KeyboardHookService.ExtraInfoAppMarker
                }
            }
        };

        SendInput(4, inputs, Marshal.SizeOf<INPUT>());

        // Restore temporarily released modifiers if physically held
        if (ctrlHeld) SendKeyDown(VirtualKeyHelper.VK_CONTROL);
        if (winHeld) SendKeyDown(VirtualKeyHelper.VK_LWIN);
    }

    public static void SendKey(int virtualKey, bool isDown, bool isExtended = false)
    {
        if (virtualKey <= 0) return;

        var scanCode = (ushort)MapVirtualKey((uint)virtualKey, 0);
        uint flags = 0;
        if (isExtended || IsExtendedVirtualKey(virtualKey)) flags |= KEYEVENTF_EXTENDEDKEY;
        if (!isDown) flags |= KEYEVENTF_KEYUP;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            mkhi = new MOUSEKEYBDHARDWAREINPUT
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)virtualKey,
                    wScan = scanCode,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = ZenithApp.Core.Input.KeyboardHookService.ExtraInfoAppMarker
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public static void SendKeyDown(int virtualKey, bool isExtended = false) => SendKey(virtualKey, true, isExtended);
    public static void SendKeyUp(int virtualKey, bool isExtended = false) => SendKey(virtualKey, false, isExtended);

    public static void SendKeyPress(int virtualKey, bool isExtended = false)
    {
        SendKeyPressCount(virtualKey, isExtended);
    }

    internal static uint SendKeyPressCount(int virtualKey, bool isExtended = false)
    {
        if (virtualKey <= 0) return 0;

        var scanCode = (ushort)MapVirtualKey((uint)virtualKey, 0);
        uint baseFlags = (isExtended || IsExtendedVirtualKey(virtualKey)) ? KEYEVENTF_EXTENDEDKEY : 0;

        var inputs = new INPUT[2];
        inputs[0] = new INPUT
        {
            type = INPUT_KEYBOARD,
            mkhi = new MOUSEKEYBDHARDWAREINPUT
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)virtualKey,
                    wScan = scanCode,
                    dwFlags = baseFlags,
                    time = 0,
                    dwExtraInfo = ZenithApp.Core.Input.KeyboardHookService.ExtraInfoAppMarker
                }
            }
        };

        inputs[1] = new INPUT
        {
            type = INPUT_KEYBOARD,
            mkhi = new MOUSEKEYBDHARDWAREINPUT
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)virtualKey,
                    wScan = scanCode,
                    dwFlags = baseFlags | KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = ZenithApp.Core.Input.KeyboardHookService.ExtraInfoAppMarker
                }
            }
        };

        return SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    public static bool IsExtendedVirtualKey(int vk)
    {
        // Arrow keys, Home, End, PageUp, PageDown, Insert, Delete, Right Ctrl, Right Alt, LWin, RWin, Apps, etc.
        return vk switch
        {
            0x21 => true, // VK_PRIOR (Page Up)
            0x22 => true, // VK_NEXT (Page Down)
            0x23 => true, // VK_END
            0x24 => true, // VK_HOME
            0x25 => true, // VK_LEFT
            0x26 => true, // VK_UP
            0x27 => true, // VK_RIGHT
            0x28 => true, // VK_DOWN
            0x2D => true, // VK_INSERT
            0x2E => true, // VK_DELETE
            0x5B => true, // VK_LWIN
            0x5C => true, // VK_RWIN
            0x5D => true, // VK_APPS
            0xA3 => true, // VK_RCONTROL
            0xA5 => true, // VK_RMENU (Right Alt)
            0x6F => true, // VK_DIVIDE (NumPad /)
            _ => false
        };
    }
}
