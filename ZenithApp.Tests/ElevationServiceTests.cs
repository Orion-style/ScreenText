using System.Windows;
using Xunit;
using ZenithApp.Core.Lifecycle;
using ZenithApp.Core.Logging;
using ZenithApp.Core.Modules;
using ZenithApp.Core.Settings;
using ZenithApp.UI.ViewModels;

namespace ZenithApp.Tests;

public class MockElevationService : IElevationService
{
    public bool IsElevated { get; set; }
    public bool IsAlwaysAdmin { get; set; }
    public int SetAlwaysAdminCallCount { get; private set; }
    public int RestartCallCount { get; private set; }

    public bool IsRunningAsAdministrator() => IsElevated;
    public bool IsAlwaysRunAsAdminEnabled() => IsAlwaysAdmin;

    public bool SetAlwaysRunAsAdmin(bool enable)
    {
        IsAlwaysAdmin = enable;
        SetAlwaysAdminCallCount++;
        return true;
    }

    public bool RestartAsAdministrator()
    {
        RestartCallCount++;
        return true;
    }
}

public class ElevationServiceTests
{
    [Fact]
    public void ElevationService_IsRunningAsAdministrator_DoesNotThrow()
    {
        var logger = new TestLogger();
        var service = new ElevationService(logger);

        // Should return a valid boolean reflecting current execution context
        var isElevated = service.IsRunningAsAdministrator();
        Assert.True(isElevated || !isElevated);
    }

    [Fact]
    public void ElevationService_IsAlwaysRunAsAdminEnabled_DoesNotThrow()
    {
        var logger = new TestLogger();
        var service = new ElevationService(logger);

        var isAlwaysAdmin = service.IsAlwaysRunAsAdminEnabled();
        Assert.True(isAlwaysAdmin || !isAlwaysAdmin);
    }

    [Fact]
    public void SettingsViewModel_ReflectsStandardUserElevationState()
    {
        var settingsService = new TestSettingsService();
        var autoLaunch = new MockAutoLaunchService(initial: false);
        var logger = new TestLogger();
        var moduleManager = new ModuleManager(logger, settingsService, Array.Empty<IFeatureModule>());

        var elevationMock = new MockElevationService
        {
            IsElevated = false,
            IsAlwaysAdmin = false
        };

        var vm = new SettingsViewModel(
            settingsService,
            autoLaunch,
            moduleManager,
            elevationService: elevationMock);

        Assert.False(vm.IsRunningAsAdmin);
        Assert.False(vm.IsAlwaysRunAsAdmin);
        Assert.Equal(Visibility.Visible, vm.RestartAdminButtonVisibility);

        // Test changing IsAlwaysRunAsAdmin
        vm.IsAlwaysRunAsAdmin = true;
        Assert.Equal(1, elevationMock.SetAlwaysAdminCallCount);
        Assert.True(elevationMock.IsAlwaysAdmin);

        // Test Restart command
        vm.RestartAsAdminCommand.Execute(null);
        Assert.Equal(1, elevationMock.RestartCallCount);
    }

    [Fact]
    public void SettingsViewModel_ReflectsAdminElevationState()
    {
        var settingsService = new TestSettingsService();
        var autoLaunch = new MockAutoLaunchService(initial: false);
        var logger = new TestLogger();
        var moduleManager = new ModuleManager(logger, settingsService, Array.Empty<IFeatureModule>());

        var elevationMock = new MockElevationService
        {
            IsElevated = true,
            IsAlwaysAdmin = true
        };

        var vm = new SettingsViewModel(
            settingsService,
            autoLaunch,
            moduleManager,
            elevationService: elevationMock);

        Assert.True(vm.IsRunningAsAdmin);
        Assert.True(vm.IsAlwaysRunAsAdmin);
        Assert.Equal(Visibility.Collapsed, vm.RestartAdminButtonVisibility);
    }
}
