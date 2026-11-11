namespace ZenithApp.Modules.StartupManager;

public enum StartupEntryLocation
{
    CurrentUserRegistry, // HKCU\...\Run
    LocalMachineRegistry, // HKLM\...\Run
    StartupFolderUser,    // Shell:Startup
    StartupFolderCommon   // Shell:Common Startup
}

public sealed class StartupEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Command { get; set; }
    public required StartupEntryLocation Location { get; init; }
    public string? FilePath { get; set; }
    public bool IsEnabled { get; set; }
    public string? Publisher { get; set; }

    public string LocationDisplayName => Location switch
    {
        StartupEntryLocation.CurrentUserRegistry => "Реестр (Текущий пользователь)",
        StartupEntryLocation.LocalMachineRegistry => "Реестр (Все пользователи)",
        StartupEntryLocation.StartupFolderUser => "Папка «Автозагрузка» (Пользователь)",
        StartupEntryLocation.StartupFolderCommon => "Папка «Автозагрузка» (Общая)",
        _ => "Система"
    };
}
