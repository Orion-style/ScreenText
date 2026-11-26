using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using ZenithApp.Core.Logging;
using ZenithApp.Modules.Screenshot;

namespace ZenithApp.Modules.NextSession;

public interface ISystemBootTimeProvider
{
    DateTime GetCurrentBootTimestampUtc();
    string? GetCurrentLogonSessionId() => null;
    DateTime? GetCurrentLogonTimestampUtc() => null;
    int? GetCurrentKernelBootId() => null;
}

public sealed class SystemBootTimeProvider : ISystemBootTimeProvider
{
    private const string PrefetchParametersSubKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters";
    private const int TokenStatisticsClass = 10;

    public DateTime GetCurrentBootTimestampUtc()
    {
        return DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    public string? GetCurrentLogonSessionId()
    {
        return TryReadLogonSession(out var sessionId, out _) ? sessionId : null;
    }

    public DateTime? GetCurrentLogonTimestampUtc()
    {
        return TryReadLogonSession(out _, out var logonTimeUtc) ? logonTimeUtc : null;
    }

    public int? GetCurrentKernelBootId()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PrefetchParametersSubKey, writable: false);
            var raw = key?.GetValue("BootId");
            if (raw is int bootId)
            {
                return bootId;
            }
            if (raw != null && int.TryParse(raw.ToString(), out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
            // Registry read is optional
        }

        return null;
    }

    private static bool TryReadLogonSession(out string? sessionId, out DateTime? logonTimeUtc)
    {
        sessionId = null;
        logonTimeUtc = null;

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var structSize = Marshal.SizeOf<TOKEN_STATISTICS>();
            if (!GetTokenInformation(identity.Token, TokenStatisticsClass, out var stats, structSize, out _))
            {
                return false;
            }

            var authId = stats.AuthenticationId;
            sessionId = $"{authId.HighPart:X8}-{authId.LowPart:X8}";

            if (LsaGetLogonSessionData(ref authId, out var buffer) == 0 && buffer != IntPtr.Zero)
            {
                try
                {
                    var data = Marshal.PtrToStructure<SECURITY_LOGON_SESSION_DATA>(buffer);
                    if (data.LogonTime > 0)
                    {
                        logonTimeUtc = DateTime.FromFileTimeUtc(data.LogonTime);
                    }
                }
                finally
                {
                    LsaFreeReturnBuffer(buffer);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_STATISTICS
    {
        public LUID TokenId;
        public LUID AuthenticationId;
        public long ExpirationTime;
        public int TokenType;
        public int ImpersonationLevel;
        public uint DynamicCharged;
        public uint DynamicAvailable;
        public uint GroupCount;
        public uint PrivilegeCount;
        public LUID ModifiedId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_LOGON_SESSION_DATA
    {
        public uint Size;
        public LUID LogonId;
        public LSA_UNICODE_STRING UserName;
        public LSA_UNICODE_STRING LogonDomain;
        public LSA_UNICODE_STRING AuthenticationPackage;
        public uint LogonType;
        public uint Session;
        public IntPtr Sid;
        public long LogonTime;
        public LSA_UNICODE_STRING LogonServer;
        public LSA_UNICODE_STRING DnsDomainName;
        public LSA_UNICODE_STRING Upn;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr TokenHandle,
        int TokenInformationClass,
        out TOKEN_STATISTICS TokenInformation,
        int TokenInformationLength,
        out int ReturnLength);

    [DllImport("secur32.dll")]
    private static extern int LsaGetLogonSessionData(ref LUID LogonId, out IntPtr ppLogonSessionData);

    [DllImport("secur32.dll")]
    private static extern int LsaFreeReturnBuffer(IntPtr Buffer);
}

public interface INextSessionLauncherService
{
    void ShowSessionNotification(string message, int launchedCount, IReadOnlyList<string> failedPrograms);
    bool LaunchProgram(NextSessionProgramEntry entry);
    bool RequestSystemRestart();
    Task<NextSessionCountdownOutcome> WaitWithCountdownAsync(int delaySeconds, int programCount, string? messagePreview, CancellationToken cancellationToken);

    Task<NextSessionCountdownOutcome> WaitWithCountdownAsync(
        int delaySeconds,
        IReadOnlyList<string> programNames,
        string? messagePreview,
        CancellationToken cancellationToken)
        => WaitWithCountdownAsync(delaySeconds, programNames.Count, messagePreview, cancellationToken);
}

public sealed class NextSessionLauncherService : INextSessionLauncherService
{
    private readonly IAppLogger _logger;

    public NextSessionLauncherService(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task<NextSessionCountdownOutcome> WaitWithCountdownAsync(
        int delaySeconds,
        int programCount,
        string? messagePreview,
        CancellationToken cancellationToken)
    {
        var placeholderNames = programCount > 0
            ? Enumerable.Range(1, programCount).Select(i => $"Программа {i}").ToList()
            : Array.Empty<string>() as IReadOnlyList<string>;

        return WaitWithCountdownAsync(delaySeconds, placeholderNames, messagePreview, cancellationToken);
    }

    public async Task<NextSessionCountdownOutcome> WaitWithCountdownAsync(
        int delaySeconds,
        IReadOnlyList<string> programNames,
        string? messagePreview,
        CancellationToken cancellationToken)
    {
        if (delaySeconds <= 0)
        {
            return NextSessionCountdownOutcome.Completed;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            return NextSessionCountdownOutcome.Completed;
        }

        try
        {
            _logger.LogInfo($"[NextSession] Showing desktop countdown overlay ({delaySeconds}s, programs: {programNames.Count}).");

            Task<NextSessionCountdownOutcome>? waitTask = null;
            await dispatcher.InvokeAsync(() =>
            {
                var overlay = new NextSessionCountdownOverlay(delaySeconds, programNames, messagePreview);
                waitTask = overlay.WaitForCompletionAsync(cancellationToken);
                overlay.Show();
            });

            if (waitTask != null)
            {
                return await waitTask;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("[NextSession] Failed to display countdown overlay; falling back to delay.", ex);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
        }

        return NextSessionCountdownOutcome.Completed;
    }

    public void ShowSessionNotification(string message, int launchedCount, IReadOnlyList<string> failedPrograms)
    {
        var hasMessage = !string.IsNullOrWhiteSpace(message);
        var parts = new List<string>();

        if (hasMessage)
        {
            parts.Add(message.Trim());
        }
        else if (launchedCount > 0)
        {
            parts.Add($"Запущено выбранных программ: {launchedCount}.");
        }

        if (failedPrograms.Count > 0)
        {
            parts.Add($"Не удалось запустить: {string.Join(", ", failedPrograms)}");
        }

        if (parts.Count == 0) return;

        var type = failedPrograms.Count > 0 && !hasMessage
            ? ToastNotificationType.Error
            : ToastNotificationType.Info;

        NotificationToast.Show(
            string.Join(Environment.NewLine, parts),
            "Следующий сеанс",
            type,
            durationSeconds: 10);
    }

    public bool LaunchProgram(NextSessionProgramEntry entry)
    {
        var target = !string.IsNullOrWhiteSpace(entry.ExecutablePath)
            ? entry.ExecutablePath.Trim()
            : entry.ProcessName.Trim();

        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        try
        {
            // Для явных локальных путей проверяем существование, чтобы не плодить системные окна ошибки
            if (Path.IsPathRooted(target) && !File.Exists(target) && !Directory.Exists(target))
            {
                _logger.LogWarning($"[NextSession] Target path not found: '{target}'.");
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = target,
                Arguments = entry.Arguments ?? string.Empty,
                UseShellExecute = true
            };

            if (Path.IsPathRooted(target) && File.Exists(target))
            {
                var workingDir = Path.GetDirectoryName(target);
                if (!string.IsNullOrWhiteSpace(workingDir))
                {
                    psi.WorkingDirectory = workingDir;
                }
            }

            Process.Start(psi);
            _logger.LogInfo($"[NextSession] Launched '{entry.Name}' ({target}).");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"[NextSession] Failed to launch '{entry.Name}' ({target}).", ex);
            return false;
        }
    }

    public bool RequestSystemRestart()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/r /t 0",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("[NextSession] Failed to initiate system restart.", ex);
            return false;
        }
    }
}
