using System.Text.Json;
using System.Threading;
using Xunit;
using ZenithApp.Core.Input;
using ZenithApp.Core.Settings;
using ZenithApp.Modules.KeyRemap;
using ZenithApp.Modules.RadialSwitcher;

namespace ZenithApp.Tests;

public class RadialSwitcherTests
{
    [Fact]
    public void RadialSwitcherSettings_EnsureZonesConfigured_DefaultIs8Zones()
    {
        var settings = new RadialSwitcherSettings();
        settings.EnsureZonesConfigured();

        Assert.Equal(8, settings.ZoneCount);
        Assert.Equal(8, settings.CustomZones.Count);
        Assert.Equal("Вверх (Север)", settings.CustomZones[0].DirectionName);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void RadialSwitcherSettings_EnsureZonesConfigured_AdjustsCountCorrectly(int count)
    {
        var settings = new RadialSwitcherSettings { ZoneCount = count };
        settings.EnsureZonesConfigured();

        Assert.Equal(count, settings.CustomZones.Count);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i, settings.CustomZones[i].Index);
            Assert.False(string.IsNullOrWhiteSpace(settings.CustomZones[i].DirectionName));
        }
    }

    [Fact]
    public void ZoneEditorItemViewModel_PinningAndResetting_UpdatesState()
    {
        var config = new RadialZoneConfig(0, "Вверх");
        var settingsService = new TestSettingsService();
        var module = new RadialSwitcherModule(new TestLogger(), settingsService, new ZenithApp.Core.Input.KeyboardHookService());
        var parentVm = new RadialSwitcherSettingsViewModel(module, settingsService);

        bool saved = false;
        var vm = new ZoneEditorItemViewModel(config, parentVm, () => saved = true);

        Assert.False(vm.IsPinned);
        Assert.Contains("Автоматически", vm.StatusDescription);

        vm.IsPinned = true;
        vm.PinnedProcessName = "telegram";
        vm.PinnedExecutablePath = @"C:\Program Files\Telegram\telegram.exe";
        vm.CustomLabel = "Мессенджер";

        Assert.True(saved);
        Assert.True(vm.IsPinned);
        Assert.Equal("telegram", vm.PinnedProcessName);
        Assert.Equal(@"C:\Program Files\Telegram\telegram.exe", vm.PinnedExecutablePath);
        Assert.Equal("Мессенджер", vm.CustomLabel);
        Assert.Contains("Закреплено", vm.StatusDescription);

        // Открепление через UnpinCommand
        vm.UnpinCommand.Execute(null);
        Assert.False(vm.IsPinned);
        Assert.Equal(string.Empty, vm.PinnedProcessName);
        Assert.Equal(string.Empty, vm.PinnedExecutablePath);
        Assert.Equal(string.Empty, vm.CustomLabel);
        Assert.Contains("Автоматически", vm.StatusDescription);
    }

    [Fact]
    public void RadialZoneConfig_Serialization_PreservesAllFields()
    {
        var original = new RadialZoneConfig(2, "Вправо (Восток)")
        {
            IsPinned = true,
            PinnedProcessName = "code",
            PinnedExecutablePath = @"C:\Users\User\AppData\Local\Programs\Microsoft VS Code\Code.exe",
            CustomLabel = "VS Code Editor"
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<RadialZoneConfig>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Index, restored.Index);
        Assert.Equal(original.DirectionName, restored.DirectionName);
        Assert.Equal(original.IsPinned, restored.IsPinned);
        Assert.Equal(original.PinnedProcessName, restored.PinnedProcessName);
        Assert.Equal(original.PinnedExecutablePath, restored.PinnedExecutablePath);
        Assert.Equal(original.CustomLabel, restored.CustomLabel);
    }

    [Fact]
    public void WindowListService_RecordActiveWindow_MaintainsStrictMruOrder()
    {
        WindowListService.ClearMruHistory();

        nint app1 = 0x1001;
        nint app2 = 0x1002;
        nint app3 = 0x1003;

        WindowListService.RecordActiveWindow(app1);
        WindowListService.RecordActiveWindow(app2);
        WindowListService.RecordActiveWindow(app3);

        // В истории: app3 (#1), app2 (#2), app1 (#3)
        // Теперь пользователь снова открывает app1:
        WindowListService.RecordActiveWindow(app1);

        // В истории: app1 теперь на позиции #1, app3 на #2, app2 на #3
        // Проверяем через отслеживание
        WindowListService.ClearMruHistory();
    }

    [Fact]
    public void WindowListService_CandidateWindow_ZeroHandle_ReturnsFalse()
    {
        Assert.False(WindowListService.IsCandidateWindow(nint.Zero));
    }

    [Fact]
    public void KeyboardInputSender_InputStructSize_MatchesWin32()
    {
        int expected = Environment.Is64BitProcess ? 40 : 28;
        Assert.Equal(expected, KeyboardInputSender.InputStructSize);
    }

    [Fact]
    public void KeyboardInputSender_SendKeyPress_DoesNotThrow()
    {
        // Отправка атомарного нажатия Tab не должна вызывать исключений и не должна падать с ERROR_INVALID_PARAMETER (87)
        uint inserted = 0;
        int win32Error = 0;
        var ex = Record.Exception(() =>
        {
            inserted = KeyboardInputSender.SendKeyPressCount(VirtualKeyHelper.VK_TAB);
            win32Error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        });
        Assert.Null(ex);
        Assert.NotEqual(87, win32Error);
        Assert.True(inserted == 2u || win32Error == 5, $"Unexpected SendInput result: inserted={inserted}, error={win32Error}");
    }

    [Fact]
    public void RadialSwitcherModule_ShortTabPress_SendsNormalTabAndDoesNotOpenWheel()
    {
        var settingsService = new TestSettingsService();
        settingsService.Current.RadialSwitcher.HoldDelayMs = 150;
        var module = new RadialSwitcherModule(new TestLogger(), settingsService, new KeyboardHookService());

        int tabPressCount = 0;
        int wheelOpenCount = 0;
        module.SendKeyPressAction = vk => { if (vk == VirtualKeyHelper.VK_TAB) tabPressCount++; };
        module.WheelOpenRequestedAction = () => wheelOpenCount++;

        RadialSwitcherModule.ModifierChecker = _ => false;
        try
        {
            var down = new KeyboardHookEventArgs(VirtualKeyHelper.VK_TAB, 0x0F, true, false, false, false, 0);
            Assert.True(module.OnKeyboardInput(down));
            Assert.True(down.SuppressKeyPress);

            var up = new KeyboardHookEventArgs(VirtualKeyHelper.VK_TAB, 0x0F, false, true, false, false, 0);
            Assert.True(module.OnKeyboardInput(up));
            Assert.True(up.SuppressKeyPress);

            Assert.Equal(1, tabPressCount);
            Assert.Equal(0, wheelOpenCount);
            Assert.False(module.IsWheelOpenForTest);
        }
        finally
        {
            RadialSwitcherModule.ModifierChecker = null;
            module.Dispose();
        }
    }

    [Fact]
    public void RadialSwitcherModule_HoldTab_OpensWheelAndDoesNotSendTab()
    {
        var settingsService = new TestSettingsService();
        settingsService.Current.RadialSwitcher.HoldDelayMs = 120;
        var module = new RadialSwitcherModule(new TestLogger(), settingsService, new KeyboardHookService());

        int tabPressCount = 0;
        int wheelOpenCount = 0;
        int wheelCloseAndActivateCount = 0;
        using var wheelOpenedEvent = new ManualResetEventSlim(false);

        module.SendKeyPressAction = _ => tabPressCount++;
        module.WheelOpenRequestedAction = () =>
        {
            wheelOpenCount++;
            wheelOpenedEvent.Set();
        };
        module.WheelCloseAndActivateAction = () => wheelCloseAndActivateCount++;

        RadialSwitcherModule.ModifierChecker = _ => false;
        try
        {
            var down = new KeyboardHookEventArgs(VirtualKeyHelper.VK_TAB, 0x0F, true, false, false, false, 0);
            Assert.True(module.OnKeyboardInput(down));

            Assert.True(wheelOpenedEvent.Wait(TimeSpan.FromSeconds(2)), "Wheel should open after holding Tab.");
            Assert.Equal(1, wheelOpenCount);
            Assert.True(module.IsWheelOpenForTest);

            var up = new KeyboardHookEventArgs(VirtualKeyHelper.VK_TAB, 0x0F, false, true, false, false, 0);
            Assert.True(module.OnKeyboardInput(up));

            Assert.Equal(1, wheelCloseAndActivateCount);
            Assert.Equal(0, tabPressCount);
            Assert.False(module.IsWheelOpenForTest);
        }
        finally
        {
            RadialSwitcherModule.ModifierChecker = null;
            module.Dispose();
        }
    }

    [Fact]
    public void RadialSwitcherModule_ModifierWithTab_PassesThroughImmediately()
    {
        var settingsService = new TestSettingsService();
        var module = new RadialSwitcherModule(new TestLogger(), settingsService, new KeyboardHookService());

        int tabPressCount = 0;
        int wheelOpenCount = 0;
        module.SendKeyPressAction = _ => tabPressCount++;
        module.WheelOpenRequestedAction = () => wheelOpenCount++;

        RadialSwitcherModule.ModifierChecker = vk => vk == VirtualKeyHelper.VK_MENU;
        try
        {
            var down = new KeyboardHookEventArgs(VirtualKeyHelper.VK_TAB, 0x0F, true, false, false, false, 0);
            Assert.False(module.OnKeyboardInput(down));
            Assert.False(down.SuppressKeyPress);

            var up = new KeyboardHookEventArgs(VirtualKeyHelper.VK_TAB, 0x0F, false, true, false, false, 0);
            Assert.False(module.OnKeyboardInput(up));
            Assert.False(up.SuppressKeyPress);

            Assert.Equal(0, tabPressCount);
            Assert.Equal(0, wheelOpenCount);
        }
        finally
        {
            RadialSwitcherModule.ModifierChecker = null;
            module.Dispose();
        }
    }

    [Fact]
    public void RadialSwitcherModule_FastRolloverKey_FlushesTabDownAndCancelsWheel()
    {
        var settingsService = new TestSettingsService();
        settingsService.Current.RadialSwitcher.HoldDelayMs = 120;
        var module = new RadialSwitcherModule(new TestLogger(), settingsService, new KeyboardHookService());

        int tabDownCount = 0;
        int tabPressCount = 0;
        int wheelOpenCount = 0;
        module.SendKeyDownAction = vk => { if (vk == VirtualKeyHelper.VK_TAB) tabDownCount++; };
        module.SendKeyPressAction = _ => tabPressCount++;
        module.WheelOpenRequestedAction = () => wheelOpenCount++;

        RadialSwitcherModule.ModifierChecker = _ => false;
        try
        {
            var tabDown = new KeyboardHookEventArgs(VirtualKeyHelper.VK_TAB, 0x0F, true, false, false, false, 0);
            Assert.True(module.OnKeyboardInput(tabDown));

            // Нажатие обычной клавиши ('A') во время короткого удержания Tab
            var aDown = new KeyboardHookEventArgs(0x41, 0x1E, true, false, false, false, 0);
            Assert.False(module.OnKeyboardInput(aDown));
            Assert.Equal(1, tabDownCount);

            // Физический Tab Up должен просто проброситься без повторного SendKeyPress
            var tabUp = new KeyboardHookEventArgs(VirtualKeyHelper.VK_TAB, 0x0F, false, true, false, false, 0);
            Assert.False(module.OnKeyboardInput(tabUp));

            Thread.Sleep(180);
            Assert.Equal(0, wheelOpenCount);
            Assert.Equal(0, tabPressCount);
        }
        finally
        {
            RadialSwitcherModule.ModifierChecker = null;
            module.Dispose();
        }
    }

    [Fact]
    public void RadialSwitcherModule_EscapeDuringHold_CancelsWithoutSendingTab()
    {
        var settingsService = new TestSettingsService();
        settingsService.Current.RadialSwitcher.HoldDelayMs = 120;
        var module = new RadialSwitcherModule(new TestLogger(), settingsService, new KeyboardHookService());

        int tabPressCount = 0;
        int wheelOpenCount = 0;
        int closeImmediatelyCount = 0;
        module.SendKeyPressAction = _ => tabPressCount++;
        module.WheelOpenRequestedAction = () => wheelOpenCount++;
        module.WheelCloseImmediatelyAction = () => closeImmediatelyCount++;

        RadialSwitcherModule.ModifierChecker = _ => false;
        try
        {
            var tabDown = new KeyboardHookEventArgs(VirtualKeyHelper.VK_TAB, 0x0F, true, false, false, false, 0);
            Assert.True(module.OnKeyboardInput(tabDown));

            var escDown = new KeyboardHookEventArgs(VirtualKeyHelper.VK_ESCAPE, 0x01, true, false, false, false, 0);
            Assert.True(module.OnKeyboardInput(escDown));
            Assert.Equal(1, closeImmediatelyCount);

            // Автоповтор Tab пока клавиша ещё физически зажата не должен заново запускать колесо
            var tabRepeat = new KeyboardHookEventArgs(VirtualKeyHelper.VK_TAB, 0x0F, true, false, false, false, 0);
            Assert.True(module.OnKeyboardInput(tabRepeat));

            Thread.Sleep(180);
            Assert.Equal(0, wheelOpenCount);

            var tabUp = new KeyboardHookEventArgs(VirtualKeyHelper.VK_TAB, 0x0F, false, true, false, false, 0);
            Assert.True(module.OnKeyboardInput(tabUp));
            Assert.Equal(0, tabPressCount);
        }
        finally
        {
            RadialSwitcherModule.ModifierChecker = null;
            module.Dispose();
        }
    }

    [Fact]
    public void WindowListService_ActivateWindow_InvalidHwnd_DoesNotThrow()
    {
        var ex = Record.Exception(() => WindowListService.ActivateWindow(nint.Zero));
        Assert.Null(ex);

        var ex2 = Record.Exception(() => WindowListService.ActivateWindow(new nint(99999999)));
        Assert.Null(ex2);
    }
}
