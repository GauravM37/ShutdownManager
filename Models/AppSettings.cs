namespace ShutdownManager.Models;

public sealed class AppSettings
{
    public int CountdownSeconds { get; set; } = 60;
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool ShowNotifications { get; set; } = true;
    public bool ConfirmImmediateActions { get; set; } = true;
    // When enabled, Shutdown/Restart use shutdown.exe /f. This can discard unsaved work.
    public bool ForceCloseApplications { get; set; }

    public AppSettings Clone() => new()
    {
        CountdownSeconds = CountdownSeconds,
        StartWithWindows = StartWithWindows,
        MinimizeToTrayOnClose = MinimizeToTrayOnClose,
        StartMinimized = StartMinimized,
        ShowNotifications = ShowNotifications,
        ConfirmImmediateActions = ConfirmImmediateActions,
        ForceCloseApplications = ForceCloseApplications
    };
}
