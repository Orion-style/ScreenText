using System.Diagnostics;
using System.IO;
using ZenithApp.Core.Logging;
using ZenithApp.Modules.Screenshot;

namespace ZenithApp.Modules.NextSession;

public interface ISystemBootTimeProvider
{
    DateTime GetCurrentBootTimestampUtc();
}

public sealed class SystemBootTimeProvider : ISystemBootTimeProvider
{
    public DateTime GetCurrentBootTimestampUtc()
    {
        return DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
    }
}

public interface INextSessionLauncherService
{
    void ShowSessionNotification(string message, int launchedCount, IReadOnlyList<string> failedPrograms);
    bool LaunchProgram(NextSessionProgramEntry entry);
    bool RequestSystemRestart();
    Task<NextSessionCountdownOutcome> WaitWithCountdownAsync(int delaySeconds, int programCount, string? messagePreview, CancellationToken cancellationToken);
}

public sealed class NextSessionLauncherService : INextSessionLauncherService
{
    private readonly IAppLogger _logger;

    public NextSessionLauncherService(IAppLogger logger)
    {
        _logger = logger;
    }

    public async Task<NextSessionCountdownOutcome> WaitWithCountdownAsync(
        int delaySeconds,
        int programCount,
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

        Task<NextSessionCountdownOutcome>? waitTask = null;
        await dispatcher.InvokeAsync(() =>
        {
            var overlay = new NextSessionCountdownOverlay(delaySeconds, programCount, messagePreview);
            waitTask = overlay.WaitForCompletionAsync(cancellationToken);
            overlay.Show();
        });

        return waitTask != null
            ? await waitTask
            : NextSessionCountdownOutcome.Completed;
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
