using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using Microsoft.Win32;
using ZenithApp.Core.Logging;

namespace ZenithApp.Core.Lifecycle;

public interface IElevationService
{
    bool IsRunningAsAdministrator();
    bool IsAlwaysRunAsAdminEnabled();
    bool SetAlwaysRunAsAdmin(bool enable);
    bool RestartAsAdministrator();
}

public sealed class ElevationService : IElevationService
{
    private const string AppCompatLayersSubKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string RunAsAdminFlag = "RUNASADMIN";
    private readonly IAppLogger? _logger;

    public ElevationService(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    public bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            _logger?.LogError("Не удалось определить уровень привилегий процесса.", ex);
            return false;
        }
    }

    public bool IsAlwaysRunAsAdminEnabled()
    {
        try
        {
            var exePath = GetCurrentExecutablePath();
            if (string.IsNullOrEmpty(exePath)) return false;

            using var key = Registry.CurrentUser.OpenSubKey(AppCompatLayersSubKey, writable: false);
            if (key == null) return false;

            var val = key.GetValue(exePath) as string;
            return !string.IsNullOrWhiteSpace(val) && val.Contains(RunAsAdminFlag, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger?.LogError("Ошибка проверки флага RUNASADMIN в AppCompatFlags.", ex);
            return false;
        }
    }

    public bool SetAlwaysRunAsAdmin(bool enable)
    {
        try
        {
            var exePath = GetCurrentExecutablePath();
            if (string.IsNullOrEmpty(exePath)) return false;

            using var key = Registry.CurrentUser.CreateSubKey(AppCompatLayersSubKey, writable: true);
            if (key == null) return false;

            var existingVal = key.GetValue(exePath) as string;

            if (enable)
            {
                if (string.IsNullOrWhiteSpace(existingVal))
                {
                    key.SetValue(exePath, "~ RUNASADMIN");
                }
                else if (!existingVal.Contains(RunAsAdminFlag, StringComparison.OrdinalIgnoreCase))
                {
                    key.SetValue(exePath, $"{existingVal.Trim()} RUNASADMIN");
                }
                _logger?.LogInfo($"Включен запуск от имени администратора для: {exePath}");
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(existingVal))
                {
                    var updated = existingVal.Replace(RunAsAdminFlag, "", StringComparison.OrdinalIgnoreCase).Trim();
                    if (string.IsNullOrWhiteSpace(updated) || updated == "~")
                    {
                        key.DeleteValue(exePath, throwOnMissingValue: false);
                    }
                    else
                    {
                        key.SetValue(exePath, updated);
                    }
                }
                _logger?.LogInfo($"Отключен запуск от имени администратора для: {exePath}");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError("Ошибка записи флага RUNASADMIN в AppCompatFlags.", ex);
            return false;
        }
    }

    public bool RestartAsAdministrator()
    {
        try
        {
            var exePath = GetCurrentExecutablePath();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                _logger?.LogError("Не удалось перезапустить от имени администратора: путь к файлу не найден.");
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            };

            _logger?.LogInfo("Запуск процесса с повышенными привилегиями (UAC)...");
            var proc = Process.Start(startInfo);
            if (proc != null)
            {
                if (Application.Current is App app)
                {
                    app.ExitApplication();
                }
                else
                {
                    Application.Current?.Shutdown(0);
                }
                return true;
            }
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Пользователь нажал "Нет" в окне подтверждения UAC
            _logger?.LogInfo("Пользователь отменил запрос контроля учетных записей (UAC).");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError("Ошибка при перезапуске приложения от имени администратора.", ex);
            return false;
        }
    }

    private static string? GetCurrentExecutablePath()
    {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            return exePath;
        }

        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
