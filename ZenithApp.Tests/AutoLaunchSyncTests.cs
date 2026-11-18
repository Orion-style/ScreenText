using System;
using System.Collections.Generic;
using Xunit;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;
using ZenithApp.Core.Startup;
using ZenithApp.Modules.StartupManager;
using ZenithApp.UI.ViewModels;

namespace ZenithApp.Tests;

public class MockAutoLaunchService : IAutoLaunchService
{
    private bool _enabled;
    public event Action<bool>? AutoLaunchChanged;

    public MockAutoLaunchService(bool initial = false)
    {
        _enabled = initial;
    }

    public bool IsAutoLaunchEnabled() => _enabled;

    public bool SetAutoLaunch(bool enable)
    {
        _enabled = enable;
        AutoLaunchChanged?.Invoke(enable);
        return true;
    }

    public void NotifyAutoLaunchChanged(bool enable)
    {
        _enabled = enable;
        AutoLaunchChanged?.Invoke(enable);
    }
}

public class AutoLaunchSyncTests
{
    [Fact]
    public void StartupItemsService_EnablingZenithApp_SynchronizesWithAutoLaunchAndSettings()
    {
        var logger = new TestLogger();
        var autoLaunch = new MockAutoLaunchService(initial: false);
        var settingsService = new TestSettingsService();
        settingsService.Current.StartWithWindows = false;

        var startupService = new StartupItemsService(logger, autoLaunch, settingsService);

        var entry = new StartupEntry
        {
            Id = "CurrentUserRegistry:ZenithApp",
            Name = "ZenithApp",
            Command = "\"C:\\Programs\\ZenithApp\\ZenithApp.exe\" --minimized",
            Location = StartupEntryLocation.CurrentUserRegistry,
            FilePath = "C:\\Programs\\ZenithApp\\ZenithApp.exe",
            IsEnabled = false,
            IsConfiguredInStartup = true
        };

        var result = startupService.SetStartupEntryEnabled(entry, true);

        Assert.True(result);
        Assert.True(entry.IsEnabled);
        Assert.True(autoLaunch.IsAutoLaunchEnabled());
        Assert.True(settingsService.Current.StartWithWindows);
        Assert.True(settingsService.Saved);
    }

    [Fact]
    public void StartupItemsService_DisablingZenithApp_SynchronizesWithAutoLaunchAndSettings()
    {
        var logger = new TestLogger();
        var autoLaunch = new MockAutoLaunchService(initial: true);
        var settingsService = new TestSettingsService();
        settingsService.Current.StartWithWindows = true;

        var startupService = new StartupItemsService(logger, autoLaunch, settingsService);

        var entry = new StartupEntry
        {
            Id = "CurrentUserRegistry:ZenithApp",
            Name = "ZenithApp",
            Command = "\"C:\\Programs\\ZenithApp\\ZenithApp.exe\" --minimized",
            Location = StartupEntryLocation.CurrentUserRegistry,
            FilePath = "C:\\Programs\\ZenithApp\\ZenithApp.exe",
            IsEnabled = true,
            IsConfiguredInStartup = true
        };

        var result = startupService.SetStartupEntryEnabled(entry, false);

        Assert.True(result);
        Assert.False(entry.IsEnabled);
        Assert.False(autoLaunch.IsAutoLaunchEnabled());
        Assert.False(settingsService.Current.StartWithWindows);
        Assert.True(settingsService.Saved);
    }

    [Fact]
    public void SettingsViewModel_InitializesFromRealAutoLaunchState_WhenSettingsJsonIsStale()
    {
        var logger = new TestLogger();
        var autoLaunch = new MockAutoLaunchService(initial: false); // Real system state: disabled in Windows
        var settingsService = new TestSettingsService();
        settingsService.Current.StartWithWindows = true; // Stale JSON says true

        var moduleManager = new ModuleManager(logger, settingsService, Array.Empty<IFeatureModule>());

        using var vm = new SettingsViewModel(
            settingsService,
            autoLaunch,
            moduleManager
        );

        // SettingsViewModel should reconcile and show the real system state
        Assert.False(vm.StartWithWindows);
        Assert.False(settingsService.Current.StartWithWindows);
    }

    [Fact]
    public void SettingsViewModel_AutoLaunchChanged_SynchronizesViewModelProperty()
    {
        var logger = new TestLogger();
        var autoLaunch = new MockAutoLaunchService(initial: false);
        var settingsService = new TestSettingsService();
        var moduleManager = new ModuleManager(logger, settingsService, Array.Empty<IFeatureModule>());

        using var vm = new SettingsViewModel(
            settingsService,
            autoLaunch,
            moduleManager
        );

        Assert.False(vm.StartWithWindows);

        // Simulate external change (e.g. from StartupManager or system)
        autoLaunch.SetAutoLaunch(true);

        Assert.True(vm.StartWithWindows);
    }
}
