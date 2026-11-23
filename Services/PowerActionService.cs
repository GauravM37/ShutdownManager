using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ShutdownManager.Models;

namespace ShutdownManager.Services;

/// <summary>
/// Executes Windows power operations. SetSuspendState requires SeShutdownPrivilege;
/// the privilege is enabled only for the current process token immediately before use.
/// </summary>
public sealed class PowerActionService
{
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;
    private const int ErrorNotAllAssigned = 1300;
    private const string ShutdownPrivilegeName = "SeShutdownPrivilege";

    private readonly Logger _logger;

    public PowerActionService(Logger logger) => _logger = logger;

    /// <summary>
    /// Returns false when Windows reports hibernation as disabled or unsupported.
    /// Used by the UI to avoid presenting Hibernate as an available immediate action.
    /// </summary>
    public bool IsHibernateAvailable
    {
        get
        {
            try
            {
                // IsPwrHibernateAllowed returns a native one-byte BOOLEAN. Query it each time;
                // do not cache this value because the user may enable/disable hibernation
                // while Shutdown Manager remains running.
                return IsPwrHibernateAllowed() != 0;
            }
            catch (Exception exception)
            {
                _logger.Warning($"Could not query Windows hibernation capability: {exception.Message}");
                return false;
            }
        }
    }

    public async Task<PowerExecutionResult> ExecuteAsync(
        PowerAction action,
        CancellationToken cancellationToken = default,
        bool forceCloseApplications = false)
    {
        try
        {
            switch (action)
            {
                case PowerAction.Shutdown:
                    return await RunShutdownCommandAsync(
                        forceCloseApplications ? "/s /f /t 0" : "/s /t 0",
                        "Shutdown", cancellationToken, forceCloseApplications);
                case PowerAction.Restart:
                    return await RunShutdownCommandAsync(
                        forceCloseApplications ? "/r /f /t 0" : "/r /t 0",
                        "Restart", cancellationToken, forceCloseApplications);
                case PowerAction.LogOff:
                    return await RunShutdownCommandAsync("/l", "Log off", cancellationToken);
                case PowerAction.Sleep:
                    return ExecuteSuspend(hibernate: false);
                case PowerAction.Hibernate:
                    return ExecuteSuspend(hibernate: true);
                case PowerAction.Lock:
                    return LockWorkStation()
                        ? PowerExecutionResult.Ok("The workstation was locked.")
                        : PowerExecutionResult.Fail("Windows could not lock the workstation. " + GetLastErrorMessage());
                default:
                    return PowerExecutionResult.Fail("Unsupported power operation.");
            }
        }
        catch (OperationCanceledException)
        {
            return PowerExecutionResult.Fail("The request was cancelled before it was issued.");
        }
        catch (Exception exception)
        {
            _logger.Error($"Power operation '{action}' failed.", exception);
            return PowerExecutionResult.Fail(exception.Message);
        }
    }

    private async Task<PowerExecutionResult> RunShutdownCommandAsync(string arguments, string operation, CancellationToken cancellationToken, bool forceCloseApplications = false)
    {
        _logger.Info($"Issuing {operation} command: shutdown.exe {arguments}. Force-close applications={forceCloseApplications}.");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };

        if (!process.Start())
            return PowerExecutionResult.Fail($"Windows did not start the {operation.ToLowerInvariant()} command.");

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode == 0)
            return PowerExecutionResult.Ok(forceCloseApplications
                ? $"Windows accepted the {operation.ToLowerInvariant()} command and was instructed to force-close running applications. Unsaved data may be lost."
                : $"Windows accepted the {operation.ToLowerInvariant()} command.");

        return PowerExecutionResult.Fail($"Windows returned exit code {process.ExitCode} for {operation.ToLowerInvariant()}.");
    }

    private PowerExecutionResult ExecuteSuspend(bool hibernate)
    {
        if (hibernate && !IsHibernateAvailable)
        {
            const string message = "Hibernate is unavailable because Windows hibernation is disabled or unsupported on this computer. " +
                "Check available states with 'powercfg /a'. If the device supports hibernation but it is disabled, open Windows Terminal as Administrator and run 'powercfg /hibernate on'.";
            _logger.Warning(message);
            return PowerExecutionResult.Fail(message);
        }

        // Microsoft documents that SetSuspendState requires SE_SHUTDOWN_NAME.
        // This privilege is commonly present in the user token but not enabled by default.
        EnableShutdownPrivilege();

        _logger.Info(hibernate
            ? "Requesting Windows Hibernate (S4)."
            : "Requesting Windows Sleep (Suspend). No hibernate flag is set by Shutdown Manager.");

        // Use the typed WinForms API so the requested state is explicit:
        // PowerState.Suspend requests Sleep; PowerState.Hibernate requests S4.
        // Windows may still apply its active Hybrid Sleep / Modern Standby policy
        // after accepting a Sleep request; this API does not override that policy.
        var requestedState = hibernate ? PowerState.Hibernate : PowerState.Suspend;
        _logger.Info($"Requesting Windows power state: {requestedState} (Sleep maps to Suspend; Hibernate maps to S4).");

        bool succeeded = Application.SetSuspendState(
            requestedState,
            force: false,
            disableWakeEvent: false);
        if (succeeded)
        {
            var message = hibernate
                ? "Windows accepted the hibernation request."
                : "Windows accepted the sleep request.";
            _logger.Info(message);
            return PowerExecutionResult.Ok(message);
        }

        var errorCode = Marshal.GetLastWin32Error();
        var errorMessage = errorCode == 0
            ? "Windows did not provide an extended error code. The device, firmware, driver or power policy may block this state."
            : new Win32Exception(errorCode).Message;
        var failure = $"Windows could not {(hibernate ? "hibernate" : "put the computer to sleep")}. Win32 error {errorCode}: {errorMessage}";
        _logger.Error(failure);
        return PowerExecutionResult.Fail(failure);
    }

    private static void EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var tokenHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the current process token to enable the Windows power-operation privilege.");

        try
        {
            if (!LookupPrivilegeValue(null, ShutdownPrivilegeName, out var luid))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not locate the Windows shutdown privilege.");

            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Privilege = new LuidAndAttributes
                {
                    Luid = luid,
                    Attributes = SePrivilegeEnabled
                }
            };

            if (!AdjustTokenPrivileges(tokenHandle, disableAllPrivileges: false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows refused to enable the shutdown privilege.");

            var adjustmentError = Marshal.GetLastWin32Error();
            if (adjustmentError == ErrorNotAllAssigned)
            {
                throw new Win32Exception(adjustmentError,
                    "This Windows account does not have the 'Shut down the system' user right. Ask your administrator to grant it, then sign out and sign in again.");
            }
        }
        finally
        {
            CloseHandle(tokenHandle);
        }
    }

    private static string GetLastErrorMessage()
    {
        var error = Marshal.GetLastWin32Error();
        return error == 0 ? "The operation may be unsupported by this computer or power policy." : new Win32Exception(error).Message;
    }

    [DllImport("powrprof.dll", ExactSpelling = true)]
    private static extern byte IsPwrHibernateAllowed();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public LuidAndAttributes Privilege;
    }
}
