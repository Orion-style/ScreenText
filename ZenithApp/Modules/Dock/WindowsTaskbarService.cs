using System;
using System.Runtime.InteropServices;
using ZenithApp.Core.Logging;

namespace ZenithApp.Modules.Dock;

public static class WindowsTaskbarService
{
    private const uint ABM_GETSTATE = 4;
    private const uint ABM_SETSTATE = 10;
    private const int ABS_AUTOHIDE = 1;
    private const int ABS_ALWAYSONTOP = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern nuint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint FindWindow(string lpClassName, string? lpWindowName);

    /// <summary>
    /// Получает текущее состояние панели задач Windows (флаги ABS_AUTOHIDE, ABS_ALWAYSONTOP).
    /// </summary>
    public static int GetTaskbarState()
    {
        try
        {
            var data = new APPBARDATA
            {
                cbSize = Marshal.SizeOf<APPBARDATA>(),
                hWnd = FindWindow("Shell_TrayWnd", null)
            };
            return (int)SHAppBarMessage(ABM_GETSTATE, ref data);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Проверяет, включено ли сейчас автоскрытие панели задач Windows.
    /// </summary>
    public static bool IsAutoHideEnabled()
    {
        var state = GetTaskbarState();
        return (state & ABS_AUTOHIDE) != 0;
    }

    /// <summary>
    /// Устанавливает состояние автоскрытия панели задач Windows (ABS_AUTOHIDE).
    /// </summary>
    public static bool SetAutoHide(bool enable, IAppLogger? logger = null)
    {
        try
        {
            var trayWnd = FindWindow("Shell_TrayWnd", null);
            var currentState = GetTaskbarState();

            int newState;
            if (enable)
            {
                newState = currentState | ABS_AUTOHIDE;
            }
            else
            {
                newState = currentState & ~ABS_AUTOHIDE;
            }

            var data = new APPBARDATA
            {
                cbSize = Marshal.SizeOf<APPBARDATA>(),
                hWnd = trayWnd,
                lParam = newState
            };

            SHAppBarMessage(ABM_SETSTATE, ref data);
            logger?.LogInfo($"[WindowsTaskbarService] Состояние автоскрытия панели задач изменено на: {enable} (raw state: {newState})");
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogError($"[WindowsTaskbarService] Ошибка установки состояния автоскрытия панели задач: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// Восстанавливает конкретное числовое состояние (сохраненное ранее).
    /// </summary>
    public static bool RestoreState(int rawState, IAppLogger? logger = null)
    {
        try
        {
            var trayWnd = FindWindow("Shell_TrayWnd", null);
            var data = new APPBARDATA
            {
                cbSize = Marshal.SizeOf<APPBARDATA>(),
                hWnd = trayWnd,
                lParam = rawState
            };

            SHAppBarMessage(ABM_SETSTATE, ref data);
            logger?.LogInfo($"[WindowsTaskbarService] Исходное состояние панели задач успешно восстановлено: {rawState}");
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogError($"[WindowsTaskbarService] Ошибка восстановления состояния панели задач: {ex.Message}", ex);
            return false;
        }
    }
}
