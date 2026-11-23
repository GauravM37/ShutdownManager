using ShutdownManager.Models;

namespace ShutdownManager.Services;

public static class ScheduleCalculator
{
    public static DateTime? GetNextOccurrence(ScheduleItem schedule, DateTime nowLocal)
    {
        if (!schedule.IsEnabled)
            return null;

        if (schedule.SnoozedUntilLocal is DateTime snoozedUntil)
            return snoozedUntil;

        switch (schedule.Type)
        {
            case ScheduleType.Daily:
            {
                var candidate = nowLocal.Date.Add(schedule.TimeOfDay.ToTimeSpan());
                if (candidate <= nowLocal)
                    candidate = candidate.AddDays(1);
                return candidate;
            }
            case ScheduleType.Weekly:
            {
                if (schedule.Weekdays.Count == 0)
                    return null;

                for (var offset = 0; offset <= 7; offset++)
                {
                    var date = nowLocal.Date.AddDays(offset);
                    if (!schedule.Weekdays.Contains(date.DayOfWeek))
                        continue;
                    var candidate = date.Add(schedule.TimeOfDay.ToTimeSpan());
                    if (candidate > nowLocal)
                        return candidate;
                }
                return null;
            }
            case ScheduleType.OneTime:
                if (schedule.IsOneTimeConsumed)
                    return null;
                return schedule.OneTimeDateTimeLocal is DateTime oneTime && oneTime > nowLocal ? oneTime : null;
            default:
                return null;
        }
    }

    public static bool TryGetDueOccurrence(
        ScheduleItem schedule,
        DateTime nowLocal,
        int graceSeconds,
        out DateTime dueAtLocal,
        out string occurrenceKey)
    {
        dueAtLocal = default;
        occurrenceKey = "";

        if (schedule.Type == ScheduleType.Daily)
        {
            dueAtLocal = nowLocal.Date.Add(schedule.TimeOfDay.ToTimeSpan());
            if (dueAtLocal <= nowLocal && (nowLocal - dueAtLocal).TotalSeconds <= graceSeconds)
            {
                occurrenceKey = "daily:" + dueAtLocal.ToString("yyyy-MM-dd");
                return true;
            }
            return false;
        }

        if (schedule.Type == ScheduleType.Weekly)
        {
            if (!schedule.Weekdays.Contains(nowLocal.DayOfWeek))
                return false;

            dueAtLocal = nowLocal.Date.Add(schedule.TimeOfDay.ToTimeSpan());
            if (dueAtLocal <= nowLocal && (nowLocal - dueAtLocal).TotalSeconds <= graceSeconds)
            {
                occurrenceKey = "weekly:" + dueAtLocal.ToString("yyyy-MM-dd");
                return true;
            }
        }

        return false;
    }
}
