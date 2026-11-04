using System;
using System.Collections.Generic;
using ZenithApp.Modules.SnapZones;

namespace ZenithApp.Tests;

public class SnapZonesModuleTests
{
    [Fact]
    public void PinnableWindow_FiltersSystemAndExcludedProcesses()
    {
        var excluded = new List<string> { "dwm", "explorer", "ZenithApp", "game" };

        Assert.False(WindowPinService.IsPinnableWindow(nint.Zero, excluded));
    }

    [Fact]
    public void SnapZonesSettings_DefaultValues()
    {
        var settings = new SnapZonesSettings();
        Assert.True(settings.ShowBorder);
        Assert.Equal(3, settings.BorderThickness);
        Assert.True(settings.PlayNotificationSound);
        Assert.Contains("explorer", settings.ExcludedProcesses);
    }
}
