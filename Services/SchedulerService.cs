using System.Threading.Channels;
using Microsoft.Win32;
using ShutdownManager.Models;

namespace ShutdownManager.Services;

/// <summary>
/// Background schedule scanner and serialized trigger dispatcher.
/// The scheduler never touches WinForms controls directly.
/// </summary>
public sealed class SchedulerService : IAsyncDisposable
{
    private readonly AppRepository _repository;
    private readonly Logger _logger;
    private readonly Func<ScheduleTrigger, CancellationToken, Task> _handleTriggerAsync;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private Channel<ScheduleTrigger>? _triggerChannel;
    private Task? _scanTask;
    private Task? _dispatchTask;

    public SchedulerService(
        AppRepository repository,
        Logger logger,
        Func<ScheduleTrigger, CancellationToken, Task> handleTriggerAsync)
    {
        _repository = repository;
        _logger = logger;
        _handleTriggerAsync = handleTriggerAsync;
    }

    public void Start()
    {
        if (_cancellation is not null)
            return;

        _cancellation = new CancellationTokenSource();
        _triggerChannel = Channel.CreateUnbounded<ScheduleTrigger>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;

        _scanTask = ScanLoopAsync(_cancellation.Token);
        _dispatchTask = DispatchLoopAsync(_cancellation.Token);
        _logger.Info("Background scheduler started.");
    }

    public async Task StopAsync()
    {
        var cancellation = _cancellation;
        if (cancellation is null)
            return;

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.TimeChanged -= OnTimeChanged;
        cancellation.Cancel();
        _triggerChannel?.Writer.TryComplete();

        var tasks = new[] { _scanTask, _dispatchTask }.Where(t => t is not null).Cast<Task>().ToArray();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Expected during normal shutdown.
        }
        catch (Exception exception)
        {
            _logger.Error("An error occurred while stopping the scheduler.", exception);
        }

        cancellation.Dispose();
        _cancellation = null;
        _scanTask = null;
        _dispatchTask = null;
        _triggerChannel = null;
        _logger.Info("Background scheduler stopped.");
    }

    private async Task ScanLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.Error("The scheduler encountered an error while checking schedules.", exception);
                try { await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task ScanOnceAsync(CancellationToken cancellationToken)
    {
        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTime.Now;
            foreach (var schedule in _repository.GetSchedulesSnapshot())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_repository.TryConsumeDue(schedule.Id, now, out var trigger) && trigger is not null)
                {
                    if (_triggerChannel is null || !_triggerChannel.Writer.TryWrite(trigger))
                        _logger.Warning($"A due trigger for schedule '{schedule.Name}' could not be queued.");
                    else
                        _logger.Info($"Queued schedule '{schedule.Name}' ({trigger.OccurrenceKey}).");
                }
            }
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private async Task DispatchLoopAsync(CancellationToken cancellationToken)
    {
        var channel = _triggerChannel;
        if (channel is null)
            return;

        try
        {
            await foreach (var trigger in channel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await _handleTriggerAsync(trigger, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.Error($"Could not handle the due schedule '{trigger.Schedule.Name}'.", exception);
                    try
                    {
                        _repository.CompleteOccurrence(trigger.Schedule.Id);
                        _repository.AddHistory(trigger.Schedule.Name, trigger.Schedule.Action.ToString(), "Error", exception.Message);
                    }
                    catch (Exception persistenceException)
                    {
                        _logger.Error("Could not record a failed scheduled operation.", persistenceException);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during application shutdown.
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _logger.Info("Windows resumed from a power-saving state; schedule checks will recalculate immediately.");
            _ = ScanAfterSystemEventAsync();
        }
    }

    private void OnTimeChanged(object? sender, EventArgs e)
    {
        _logger.Info("The system clock changed; schedule checks will recalculate immediately.");
        _ = ScanAfterSystemEventAsync();
    }

    private async Task ScanAfterSystemEventAsync()
    {
        var token = _cancellation?.Token ?? CancellationToken.None;
        if (token.IsCancellationRequested)
            return;

        try
        {
            await ScanOnceAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Scheduler is stopping.
        }
        catch (Exception exception)
        {
            _logger.Error("Immediate schedule recalculation failed.", exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _scanGate.Dispose();
    }
}
