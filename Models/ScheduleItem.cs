namespace ShutdownManager.Models;

public sealed class ScheduleItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New schedule";
    public bool IsEnabled { get; set; } = true;
    public ScheduleType Type { get; set; } = ScheduleType.Daily;
    public PowerAction Action { get; set; } = PowerAction.Shutdown;
    public TimeOnly TimeOfDay { get; set; } = new(23, 0);
    public List<DayOfWeek> Weekdays { get; set; } = new();
    public DateTime? OneTimeDateTimeLocal { get; set; }
    public DateTime CreatedAtLocal { get; set; } = DateTime.Now;

    // Scheduler bookkeeping is persisted to prevent duplicate execution after restart.
    public string? LastHandledOccurrenceKey { get; set; }
    public DateTime? LastTriggeredAtLocal { get; set; }
    public bool IsOneTimeConsumed { get; set; }

    // Snooze is stored as local wall-clock time and survives application restarts.
    public DateTime? SnoozedUntilLocal { get; set; }
    public string? SnoozedOccurrenceKey { get; set; }

    public ScheduleItem Clone() => new()
    {
        Id = Id,
        Name = Name,
        IsEnabled = IsEnabled,
        Type = Type,
        Action = Action,
        TimeOfDay = TimeOfDay,
        Weekdays = Weekdays?.ToList() ?? new List<DayOfWeek>(),
        OneTimeDateTimeLocal = OneTimeDateTimeLocal,
        CreatedAtLocal = CreatedAtLocal,
        LastHandledOccurrenceKey = LastHandledOccurrenceKey,
        LastTriggeredAtLocal = LastTriggeredAtLocal,
        IsOneTimeConsumed = IsOneTimeConsumed,
        SnoozedUntilLocal = SnoozedUntilLocal,
        SnoozedOccurrenceKey = SnoozedOccurrenceKey
    };
}
