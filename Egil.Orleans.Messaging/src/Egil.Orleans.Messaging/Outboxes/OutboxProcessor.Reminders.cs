namespace Egil.Orleans.Messaging.Outboxes;

public sealed partial class OutboxProcessor<TOutbox>
{
    private async Task InitializeReminderAfterLifecycleStartAsync()
    {
        try
        {
            await ((IOutboxComponent)this).OnActivateAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Synchronous attachment cannot await storage. Observe the failure;
            // a subsequent post or orderly shutdown retries registration.
            LogActivationReminderFailed(exception, owner.GrainContext.GrainId, grainType, reminderName);
        }
    }

    Task IOutboxComponent.OnActivateAsync(CancellationToken cancellationToken) =>
        options.ReminderPolicy == OutboxReminderPolicy.KeepRegistered
            ? EnsureInitialReminderAsync().WaitAsync(cancellationToken)
            : Task.CompletedTask;

    private Task EnsureInitialReminderAsync() => RunReminderOperationAsync(async () =>
    {
        if (reminder is null || reminderPeriod is null)
        {
            await RegisterReminderIfNeededAsync(options.IdleReminderPeriod);
        }
    });

    private Task EnsureReminderAsync() =>
        RunReminderOperationAsync(() => RegisterReminderIfNeededAsync(options.ActiveReminderPeriod));

    private async Task RegisterReminderIfNeededAsync(TimeSpan period)
    {
        if (reminder is not null && reminderPeriod == period)
        {
            return;
        }

        reminder = await owner.RegisterOrUpdateReminder(reminderName, period, period);
        reminderPeriod = period;
    }

    private Task ReconcileIdleReminderAsync() =>
        options.ReminderPolicy == OutboxReminderPolicy.OnDeactivation
            ? TryRemoveIdleReminderAsync(CancellationToken.None)
            : RunReminderOperationAsync(async () =>
            {
                if (pendingAcknowledgement is null && GetPendingItems().IsDefaultOrEmpty)
                {
                    await RegisterReminderIfNeededAsync(options.IdleReminderPeriod);
                }
            });

    private async Task RunReminderOperationAsync(Func<Task> action)
    {
        // Check state inside this gate, not before awaiting a separate wait helper:
        // several callers can resume together after a registration completes.
        // A delayed removal must not delete a new registration for returning work.
        while (reminderOperation is { } operation)
        {
            await operation;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reminderOperation = completion.Task;
        try
        {
            await action();
            completion.SetResult();
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
            // Observe the shared failure even when no other caller joined it.
            _ = completion.Task.Exception;
            throw;
        }
        finally
        {
            reminderOperation = null;
        }
    }

    private async Task TryRemoveIdleReminderAsync(CancellationToken cancellationToken)
    {
        if (reminderOperation is null && reminder is null && !reminderTicked)
        {
            return;
        }

        try
        {
            await RunReminderOperationAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!GetPendingItems().IsDefaultOrEmpty || pendingAcknowledgement is not null)
                {
                    return;
                }

                // Ordinary successful posts do no reminder I/O. Only a delivered
                // inherited tick justifies a lookup when we lack a cleanup handle.
                var idleReminder = reminder;
                if (idleReminder is null && reminderTicked)
                {
                    idleReminder = await owner.GetReminder(reminderName);
                }

                if (idleReminder is not null)
                {
                    await owner.UnregisterReminder(idleReminder);
                }

                reminder = null;
                reminderPeriod = null;
                reminderTicked = false;
            }).WaitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // A lost removal response may mean storage already deleted the row.
            // Retain the cleanup handle, but require an upsert if work returns.
            reminderPeriod = null;
            // Cleanup can be retried by a later drain, tick or deactivation. It
            // must not turn a successfully acknowledged delivery into a failure.
            LogReminderRemovalFailed(exception, owner.GrainContext.GrainId, grainType, reminderName);
        }
    }
}
