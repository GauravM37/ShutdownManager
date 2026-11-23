using System.Text.Json;
using ShutdownManager.Models;

namespace ShutdownManager.Services;

/// <summary>
/// Thread-safe in-memory configuration repository. Every mutation is persisted atomically.
/// </summary>
public sealed class AppRepository
{
    private const int MaximumHistoryEntries = 500;
    private readonly object _gate = new();
    private readonly JsonConfigStore _store;
    private readonly Logger _logger;
    private AppConfig _config = new();
    private readonly JsonSerializerOptions _cloneOptions = new() { PropertyNameCaseInsensitive = true };

    public AppRepository(JsonConfigStore store, Logger logger)
    {
        _store = store;
        _logger = logger;
    }

    public void Load()
    {
        lock (_gate)
        {
            _config = _store.Load();
            NormalizeConfig(_config);

            // A one-time schedule consumed before an application crash is not replayed.
            foreach (var schedule in _config.Schedules.Where(s => s.Type == ScheduleType.OneTime && s.IsOneTimeConsumed && s.SnoozedUntilLocal is null))
                schedule.IsEnabled = false;

            PersistLocked();
        }
    }

    public AppConfig GetSnapshot()
    {
        lock (_gate)
            return Clone(_config);
    }

    public List<ScheduleItem> GetSchedulesSnapshot()
    {
        lock (_gate)
            return _config.Schedules.Select(s => s.Clone()).ToList();
    }

    public void AddSchedule(ScheduleItem schedule)
    {
        lock (_gate)
        {
            _config.Schedules.Add(schedule.Clone());
            PersistLocked();
        }
    }

    public void UpdateSchedule(ScheduleItem schedule)
    {
        lock (_gate)
        {
            var index = _config.Schedules.FindIndex(s => s.Id == schedule.Id);
            if (index < 0)
                throw new InvalidOperationException("The selected schedule no longer exists.");

            var replacement = schedule.Clone();
            // Preserve scheduler bookkeeping for ordinary edits. However, changing
            // the date/time of a one-time schedule creates a new occurrence and
            // must discard the previous occurrence's consumed/cancelled/snoozed
            // state. Otherwise the old IsOneTimeConsumed flag permanently hides
            // the rescheduled item from the dashboard and scheduler.
            var existing = _config.Schedules[index];
            var isRescheduledOneTime = replacement.Type == ScheduleType.OneTime
                && replacement.OneTimeDateTimeLocal.HasValue
                && (existing.Type != ScheduleType.OneTime
                    || existing.OneTimeDateTimeLocal != replacement.OneTimeDateTimeLocal);

            if (isRescheduledOneTime && replacement.OneTimeDateTimeLocal.Value > DateTime.Now)
            {
                replacement.LastHandledOccurrenceKey = null;
                replacement.LastTriggeredAtLocal = null;
                replacement.IsOneTimeConsumed = false;
                replacement.SnoozedUntilLocal = null;
                replacement.SnoozedOccurrenceKey = null;
                // A completed or cancelled one-time occurrence is disabled by
                // the scheduler. Rescheduling it is an explicit request to run
                // it again, so reactivate it automatically.
                replacement.IsEnabled = true;
            }
            else
            {
                replacement.LastHandledOccurrenceKey = existing.LastHandledOccurrenceKey;
                replacement.LastTriggeredAtLocal = existing.LastTriggeredAtLocal;
                replacement.IsOneTimeConsumed = existing.IsOneTimeConsumed;
                replacement.SnoozedUntilLocal = existing.SnoozedUntilLocal;
                replacement.SnoozedOccurrenceKey = existing.SnoozedOccurrenceKey;
            }

            _config.Schedules[index] = replacement;
            PersistLocked();
        }
    }

    public void DeleteSchedule(Guid id)
    {
        lock (_gate)
        {
            _config.Schedules.RemoveAll(s => s.Id == id);
            PersistLocked();
        }
    }

    public void SetScheduleEnabled(Guid id, bool enabled)
    {
        lock (_gate)
        {
            var schedule = _config.Schedules.FirstOrDefault(s => s.Id == id);
            if (schedule is null)
                return;
            schedule.IsEnabled = enabled;
            if (!enabled)
            {
                schedule.SnoozedUntilLocal = null;
                schedule.SnoozedOccurrenceKey = null;
            }
            PersistLocked();
        }
    }

    public void SetAllSchedulesEnabled(bool enabled)
    {
        lock (_gate)
        {
            foreach (var schedule in _config.Schedules)
            {
                schedule.IsEnabled = enabled;
                if (!enabled)
                {
                    schedule.SnoozedUntilLocal = null;
                    schedule.SnoozedOccurrenceKey = null;
                }
            }
            PersistLocked();
        }
    }

    public bool AreAnySchedulesEnabled()
    {
        lock (_gate)
            return _config.Schedules.Any(s => s.IsEnabled);
    }

    /// <summary>
    /// Atomically claims a due occurrence before it is sent to the UI countdown queue.
    /// A 30-second grace window tolerates timer jitter without replaying stale events.
    /// </summary>
    public bool TryConsumeDue(Guid scheduleId, DateTime nowLocal, out ScheduleTrigger? trigger)
    {
        const int graceSeconds = 30;
        trigger = null;

        lock (_gate)
        {
            var schedule = _config.Schedules.FirstOrDefault(s => s.Id == scheduleId);
            if (schedule is null || !schedule.IsEnabled)
                return false;

            if (schedule.SnoozedUntilLocal is DateTime snoozedUntil)
            {
                if (snoozedUntil > nowLocal)
                    return false;

                if ((nowLocal - snoozedUntil).TotalSeconds > graceSeconds)
                {
                    schedule.SnoozedUntilLocal = null;
                    schedule.SnoozedOccurrenceKey = null;
                    if (schedule.Type == ScheduleType.OneTime)
                        schedule.IsEnabled = false;
                    AddHistoryLocked(schedule.Name, schedule.Action.ToString(), "Snooze expired", "The application was not running when the snoozed time passed.");
                    PersistLocked();
                    return false;
                }

                var snoozeKey = "snooze:" + snoozedUntil.Ticks;
                if (schedule.LastHandledOccurrenceKey == snoozeKey)
                {
                    schedule.SnoozedUntilLocal = null;
                    schedule.SnoozedOccurrenceKey = null;
                    PersistLocked();
                    return false;
                }

                schedule.LastHandledOccurrenceKey = snoozeKey;
                schedule.LastTriggeredAtLocal = nowLocal;
                schedule.SnoozedUntilLocal = null;
                schedule.SnoozedOccurrenceKey = null;
                trigger = new ScheduleTrigger(schedule.Clone(), snoozeKey, snoozedUntil);
                PersistLocked();
                return true;
            }

            if (schedule.Type == ScheduleType.OneTime)
            {
                if (schedule.IsOneTimeConsumed || schedule.OneTimeDateTimeLocal is not DateTime oneTime)
                    return false;

                if (oneTime > nowLocal)
                    return false;

                if ((nowLocal - oneTime).TotalSeconds > graceSeconds)
                {
                    schedule.IsEnabled = false;
                    schedule.IsOneTimeConsumed = true;
                    AddHistoryLocked(schedule.Name, schedule.Action.ToString(), "Missed / expired", "The one-time schedule had already passed when the scheduler checked it. It was not executed.");
                    PersistLocked();
                    return false;
                }

                var key = "once:" + oneTime.Ticks;
                if (schedule.LastHandledOccurrenceKey == key)
                    return false;

                schedule.LastHandledOccurrenceKey = key;
                schedule.LastTriggeredAtLocal = nowLocal;
                schedule.IsOneTimeConsumed = true;
                trigger = new ScheduleTrigger(schedule.Clone(), key, oneTime);
                PersistLocked();
                return true;
            }

            if (!ScheduleCalculator.TryGetDueOccurrence(schedule, nowLocal, graceSeconds, out var dueAt, out var occurrenceKey))
                return false;

            if (schedule.LastHandledOccurrenceKey == occurrenceKey)
                return false;

            schedule.LastHandledOccurrenceKey = occurrenceKey;
            schedule.LastTriggeredAtLocal = nowLocal;
            trigger = new ScheduleTrigger(schedule.Clone(), occurrenceKey, dueAt);
            PersistLocked();
            return true;
        }
    }

    public void SnoozeSchedule(Guid scheduleId, int minutes)
    {
        if (minutes is not (5 or 10 or 15))
            throw new ArgumentOutOfRangeException(nameof(minutes), "Snooze must be 5, 10 or 15 minutes.");

        lock (_gate)
        {
            var schedule = _config.Schedules.FirstOrDefault(s => s.Id == scheduleId);
            if (schedule is null)
                return;
            schedule.SnoozedUntilLocal = DateTime.Now.AddMinutes(minutes);
            schedule.SnoozedOccurrenceKey = schedule.LastHandledOccurrenceKey;
            schedule.IsEnabled = true;
            PersistLocked();
        }
    }

    public void CompleteOccurrence(Guid scheduleId)
    {
        lock (_gate)
        {
            var schedule = _config.Schedules.FirstOrDefault(s => s.Id == scheduleId);
            if (schedule is null)
                return;
            if (schedule.Type == ScheduleType.OneTime && schedule.IsOneTimeConsumed && schedule.SnoozedUntilLocal is null)
                schedule.IsEnabled = false;
            PersistLocked();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        lock (_gate)
        {
            _config.Settings = settings.Clone();
            _config.Settings.CountdownSeconds = Math.Clamp(_config.Settings.CountdownSeconds, 1, 86_400);
            PersistLocked();
        }
    }

    public void AddHistory(string scheduleName, string action, string result, string details)
    {
        lock (_gate)
        {
            AddHistoryLocked(scheduleName, action, result, details);
            PersistLocked();
        }
    }

    private void AddHistoryLocked(string scheduleName, string action, string result, string details)
    {
        _config.History.Insert(0, new ShutdownHistoryEntry
        {
            TimestampLocal = DateTime.Now,
            ScheduleName = scheduleName,
            Action = action,
            Result = result,
            Details = details
        });

        if (_config.History.Count > MaximumHistoryEntries)
            _config.History.RemoveRange(MaximumHistoryEntries, _config.History.Count - MaximumHistoryEntries);
    }

    private void PersistLocked()
    {
        try
        {
            _store.Save(_config);
        }
        catch (Exception exception)
        {
            _logger.Error("Could not save application data.", exception);
            throw;
        }
    }

    private AppConfig Clone(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, _cloneOptions);
        return JsonSerializer.Deserialize<AppConfig>(json, _cloneOptions) ?? new AppConfig();
    }

    private static void NormalizeConfig(AppConfig config)
    {
        config.Settings ??= new AppSettings();
        config.Settings.CountdownSeconds = Math.Clamp(config.Settings.CountdownSeconds, 1, 86_400);
        config.Schedules ??= new List<ScheduleItem>();
        config.History ??= new List<ShutdownHistoryEntry>();

        foreach (var schedule in config.Schedules)
        {
            schedule.Name = string.IsNullOrWhiteSpace(schedule.Name) ? "Unnamed schedule" : schedule.Name.Trim();
            schedule.Weekdays ??= new List<DayOfWeek>();
            schedule.Weekdays = schedule.Weekdays.Distinct().ToList();
        }

        if (config.History.Count > MaximumHistoryEntries)
            config.History = config.History.Take(MaximumHistoryEntries).ToList();
    }
}
