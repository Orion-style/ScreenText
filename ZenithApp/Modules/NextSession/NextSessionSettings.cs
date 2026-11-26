using System.Windows.Input;
using ZenithApp.Core.Hotkeys;

namespace ZenithApp.Modules.NextSession;

public sealed class NextSessionProgramEntry
{
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;

    public string GetNormalizedKey()
    {
        if (!string.IsNullOrWhiteSpace(ExecutablePath))
        {
            return ExecutablePath.Trim().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(ProcessName))
        {
            return ProcessName.Trim().ToUpperInvariant();
        }

        return Name.Trim().ToUpperInvariant();
    }
}

public sealed class NextSessionSettings
{
    public KeyModifiers HotkeyModifiers { get; set; } = KeyModifiers.Control | KeyModifiers.Alt;
    public Key HotkeyKey { get; set; } = Key.N;

    public bool TriggerOnResumeFromSleep { get; set; } = false;

    public int StartupDelaySeconds { get; set; } = 3;
    public int LaunchIntervalMs { get; set; } = 500;

    public bool HasPendingPlan { get; set; } = false;
    public string PendingMessage { get; set; } = string.Empty;
    public List<NextSessionProgramEntry> PendingPrograms { get; set; } = new();
    public List<NextSessionProgramEntry> CustomPrograms { get; set; } = new();

    public DateTime? PlannedBootTimestampUtc { get; set; }
    public string? PlannedLogonSessionId { get; set; }
    public int? PlannedKernelBootId { get; set; }
    public bool SystemShutdownObserved { get; set; } = false;
    public DateTime? PlannedAtUtc { get; set; }
    public DateTime? LastExecutedAtUtc { get; set; }
}
