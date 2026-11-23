namespace ShutdownManager.Models;

public sealed class ShutdownHistoryEntry
{
    public DateTime TimestampLocal { get; set; } = DateTime.Now;
    public string ScheduleName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Result { get; set; } = "";
    public string Details { get; set; } = "";
}
