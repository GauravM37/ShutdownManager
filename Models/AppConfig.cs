namespace ShutdownManager.Models;

public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public List<ScheduleItem> Schedules { get; set; } = new();
    public List<ShutdownHistoryEntry> History { get; set; } = new();
}
