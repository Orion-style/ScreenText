using System;
using System.Collections.Generic;
using System.Linq;
using ZenithApp.Core.Logging;
using ZenithApp.Modules.StartupManager;
using Xunit;

namespace ZenithApp.Tests;

public class StartupManagerTests
{
    private sealed class MockLogger : IAppLogger
    {
        public void Log(LogLevel level, string message, Exception? exception = null) { }
    }

    private sealed class FakeStartupItemsService : IStartupItemsService
    {
        public List<StartupEntry> Entries { get; } = new();

        public IReadOnlyList<StartupEntry> GetStartupEntries()
        {
            return Entries;
        }

        public bool SetStartupEntryEnabled(StartupEntry entry, bool enable)
        {
            entry.IsEnabled = enable;
            return true;
        }

        public bool RemoveStartupEntry(StartupEntry entry)
        {
            return Entries.Remove(entry);
        }
    }

    [Fact]
    public void StartupManagerModule_Properties_AreCorrect()
    {
        var logger = new MockLogger();
        var module = new StartupManagerModule(logger);

        Assert.Equal("startup_manager", module.Id);
        Assert.False(string.IsNullOrWhiteSpace(module.Name));
        Assert.False(string.IsNullOrWhiteSpace(module.Description));
        Assert.False(module.IsEnabled);
    }

    [Fact]
    public void StartupManagerViewModel_LoadsAndFiltersEntries()
    {
        var logger = new MockLogger();
        var module = new StartupManagerModule(logger);
        var service = new FakeStartupItemsService();

        service.Entries.Add(new StartupEntry
        {
            Id = "1",
            Name = "Discord",
            Command = "C:\\Discord\\app.exe",
            Location = StartupEntryLocation.CurrentUserRegistry,
            IsEnabled = true,
            Publisher = "Discord Inc."
        });

        service.Entries.Add(new StartupEntry
        {
            Id = "2",
            Name = "Telegram",
            Command = "C:\\Telegram\\telegram.exe",
            Location = StartupEntryLocation.CurrentUserRegistry,
            IsEnabled = false,
            Publisher = "Telegram FZ-LLC"
        });

        var vm = new StartupManagerViewModel(module, service);

        Assert.Equal(2, vm.TotalCount);
        Assert.Equal(1, vm.EnabledCount);
        Assert.Equal(2, vm.FilteredItems.Count);

        // Filter test
        vm.SearchFilter = "tele";
        Assert.Single(vm.FilteredItems);
        Assert.Equal("Telegram", vm.FilteredItems[0].Name);

        // Toggle state test
        vm.FilteredItems[0].IsEnabled = true;
        Assert.True(vm.FilteredItems[0].IsEnabled);
        Assert.Equal(2, vm.EnabledCount);
    }

    [Fact]
    public void StartupItemViewModel_Delete_RemovesFromCollection()
    {
        var logger = new MockLogger();
        var module = new StartupManagerModule(logger);
        var service = new FakeStartupItemsService();

        service.Entries.Add(new StartupEntry
        {
            Id = "1",
            Name = "AppToDelete",
            Command = "C:\\App\\app.exe",
            Location = StartupEntryLocation.CurrentUserRegistry,
            IsEnabled = true
        });

        var vm = new StartupManagerViewModel(module, service);
        Assert.Single(vm.Items);

        vm.Items[0].DeleteCommand.Execute(null);

        Assert.Empty(vm.Items);
        Assert.Equal(0, vm.TotalCount);
        Assert.Equal(0, vm.EnabledCount);
    }
}
