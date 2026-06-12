namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Marker interface for grains that use <see cref="OutboxProcessor{TOutbox}"/>.
/// Provides a default interface method (DIM) for <see cref="IRemindable.ReceiveReminder"/>
/// that routes reminder callbacks to the processor automatically.
/// </summary>
/// <remarks>
/// <para>
/// <b>Zero ceremony:</b> Implementing this interface is one of two obligations
/// for grains using the outbox pattern (the other is calling
/// <c>RegisterOutboxProcessor</c> in <c>OnActivateAsync</c>). No manual
/// <c>ReceiveReminder</c> override is needed — the DIM discovers the
/// <see cref="IOutboxComponent"/> registered on the grain context and
/// forwards the callback.
/// </para>
/// <para>
/// <b>Escape hatch:</b> Grains with their own reminders can override
/// <see cref="IRemindable.ReceiveReminder"/> explicitly, handle their own
/// reminder names, and forward unknown names to the processor:
/// <code>
/// public async Task ReceiveReminder(string name, TickStatus status)
/// {
///     if (name == MyOwnReminder) { await DoMyWork(); return; }
///     await outboxProcessor.ReceiveReminderAsync(name, status);
/// }
/// </code>
/// </para>
/// <para>
/// <b>Compiler enforcement:</b> The <c>RegisterOutboxProcessor</c>
/// extension method constrains <c>TGrain : IOutboxGrain, IGrainBase</c>,
/// so grains that forget to implement this interface get a compile error.
/// </para>
/// </remarks>
public interface IOutboxGrain : IRemindable
{
    /// <summary>
    /// Default implementation that discovers the <see cref="IOutboxComponent"/>
    /// on the grain context and forwards the reminder callback. Throws if no
    /// processor is attached, since that means the grain implemented
    /// <see cref="IOutboxGrain"/> but did not call
    /// <c>RegisterOutboxProcessor</c> during activation.
    /// </summary>
    Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        var grainBase = (IGrainBase)this;
        var component = grainBase.GrainContext.GetComponent<IOutboxComponent>();
        if (component is null)
        {
            throw new InvalidOperationException(
                $"No {nameof(OutboxProcessor<object>)} is attached to the grain context. Call RegisterOutboxProcessor(...) from OnActivateAsync before outbox reminders are received.");
        }

        return component.ReceiveReminderAsync(reminderName, status).AsTask();
    }
}