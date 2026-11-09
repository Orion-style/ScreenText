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
        vm.CustomLabel = "Мессенджер";

        Assert.True(saved);
        Assert.True(vm.IsPinned);
        Assert.Equal("telegram", vm.PinnedProcessName);
        Assert.Equal("Мессенджер", vm.CustomLabel);
        Assert.Contains("Закреплено", vm.StatusDescription);

        // Сброс
        vm.ResetZoneCommand.Execute(null);
        Assert.False(vm.IsPinned);
        Assert.Equal(string.Empty, vm.PinnedProcessName);
        Assert.Equal(string.Empty, vm.CustomLabel);
        Assert.Contains("Автоматически", vm.StatusDescription);
    }

    [Fact]
    public void WindowListService_CandidateWindow_ZeroHandle_ReturnsFalse()
    {
        Assert.False(WindowListService.IsCandidateWindow(nint.Zero));
    }
}
