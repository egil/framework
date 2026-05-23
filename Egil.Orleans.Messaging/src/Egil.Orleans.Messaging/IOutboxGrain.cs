namespace Egil.Orleans.Messaging;

/// <summary>
/// Marker interface for grains that use <see cref="OutboxProcessor{TOutbox}"/>.
/// Provides a default interface method (DIM) for <see cref="IRemindable.ReceiveReminder"/>
/// that routes reminder callbacks to the processor automatically.
/// </summary>
/// <remarks>
/// <para>
/// <b>Zero ceremony:</b> Implementing this interface is one of two obligations
/// for grains using the outbox pattern (the other is calling
/// <c>InitializeOutboxProcessor</c> in <c>OnActivateAsync</c>). No manual
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
/// <b>Compiler enforcement:</b> The <c>InitializeOutboxProcessor</c>
/// extension method constrains <c>TGrain : IOutboxGrain, IGrainBase</c>,
/// so grains that forget to implement this interface get a compile error.
/// </para>
/// </remarks>
public interface IOutboxGrain : IRemindable
{
    /// <summary>
    /// Default implementation that discovers the <see cref="IOutboxComponent"/>
    /// on the grain context and forwards the reminder callback. Returns
    /// <see cref="Task.CompletedTask"/> if no processor is attached (safe
    /// no-op for reminders that fire before activation completes).
    /// </summary>
    Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        var grainBase = (IGrainBase)this;
        var component = grainBase.GrainContext.GetComponent<IOutboxComponent>();
        return component is null
            ? Task.CompletedTask
            : component.ReceiveReminderAsync(reminderName, status).AsTask();
    }
}
