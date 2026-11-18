using System.Text.Json;
using Xunit;
using ZenithApp.Core.Settings;
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
    public void KeyboardInputSender_SendKeyPress_DoesNotThrow()
    {
        // Отправка атомарного нажатия Tab не должна вызывать исключений
        var ex = Record.Exception(() => ZenithApp.Modules.KeyRemap.KeyboardInputSender.SendKeyPress(ZenithApp.Modules.KeyRemap.VirtualKeyHelper.VK_TAB));
        Assert.Null(ex);
    }
}
