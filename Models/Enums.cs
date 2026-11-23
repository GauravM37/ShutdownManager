namespace ShutdownManager.Models;

public enum ScheduleType
{
    Daily,
    Weekly,
    OneTime
}

public enum PowerAction
{
    Shutdown,
    Restart,
    Sleep,
    Hibernate,
    LogOff,
    Lock
}

public enum CountdownOutcomeKind
{
    Executed,
    Cancelled,
    Snoozed
}

public sealed record CountdownOutcome(CountdownOutcomeKind Kind, int SnoozeMinutes = 0)
{
    public static CountdownOutcome Executed() => new(CountdownOutcomeKind.Executed);
    public static CountdownOutcome Cancelled() => new(CountdownOutcomeKind.Cancelled);
    public static CountdownOutcome Snoozed(int minutes) => new(CountdownOutcomeKind.Snoozed, minutes);
}

public sealed record ScheduleTrigger(ScheduleItem Schedule, string OccurrenceKey, DateTime DueAtLocal);

public sealed record PowerExecutionResult(bool Success, string Message)
{
    public static PowerExecutionResult Ok(string message) => new(true, message);
    public static PowerExecutionResult Fail(string message) => new(false, message);
}
