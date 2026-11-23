using Microsoft.Win32;
using System.Security;

namespace ShutdownManager.Services;

public sealed class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ShutdownManager";
    private const string LegacyValueName = "ScheduledShutdownManager";
    private readonly Logger _logger;

    public StartupManager(Logger logger) => _logger = logger;

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
                return false;

            var currentValue = key.GetValue(ValueName) as string;
            if (!string.IsNullOrWhiteSpace(currentValue))
                return true;

            // Migrate the previous product-name entry to the renamed executable.
            var legacyValue = key.GetValue(LegacyValueName) as string;
            if (string.IsNullOrWhiteSpace(legacyValue))
                return false;

            var executablePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                var startMinimized = legacyValue.Contains("--minimized", StringComparison.OrdinalIgnoreCase);
                key.SetValue(
                    ValueName,
                    startMinimized ? $"\"{executablePath}\" --minimized" : $"\"{executablePath}\"",
                    RegistryValueKind.String);
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
                _logger.Info("Migrated the legacy Windows startup entry to Shutdown Manager.");
            }

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException)
        {
            _logger.Error("Could not read Windows startup registration.", exception);
            return false;
        }
    }

    public void SetEnabled(bool enabled, bool startMinimized = true)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("Windows did not allow access to the current-user startup registry key.");

            // Remove the old value as well so an upgrade cannot start the old executable twice.
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);

            if (enabled)
            {
                var executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath))
                    throw new InvalidOperationException("Could not determine the application executable path.");

                key.SetValue(ValueName, startMinimized ? $"\"{executablePath}\" --minimized" : $"\"{executablePath}\"", RegistryValueKind.String);
                _logger.Info("Windows startup registration enabled for the current user.");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                _logger.Info("Windows startup registration removed for the current user.");
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException or InvalidOperationException)
        {
            _logger.Error("Could not update Windows startup registration.", exception);
            throw;
        }
    }
}
