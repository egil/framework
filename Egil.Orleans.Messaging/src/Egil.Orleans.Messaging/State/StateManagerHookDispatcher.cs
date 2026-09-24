using System.Runtime.ExceptionServices;

namespace Egil.Orleans.Messaging.State;

// This boundary reports user failures without letting the storage recovery code
// mistake them for an ambiguous persistence outcome.
internal sealed class StateManagerHookDispatcher<T> where T : class, IEquatable<T>
{
    private readonly AsyncLocal<int> invocationDepth = new();

    public bool IsInvoking => invocationDepth.Value != 0;

    public async Task InvokeAsync(StateManagerHooks<T> hooks, T state, StateManagerOperation operation,
        bool recordExists, CancellationToken cancellationToken, Exception? storageError = null)
    {
        List<Exception>? errors = storageError is null ? null : [storageError];
        // Reject calls originating in this handler, including after awaits, without
        // rejecting independent reentrant turns. Each dispatch retains its own flow's
        // depth, so another dispatch completing cannot release this handler's guard.
        invocationDepth.Value++;
        try
        {
            try
            {
                hooks.OnChange?.Invoke(state, operation, recordExists);
                if (hooks.OnChangeAsync is { } common)
                {
                    await common(state, operation, recordExists, cancellationToken);
                }
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }

            // A failed common handler must not suppress the operation-specific work,
            // even when its failure is cancellation after a durable commit.
            try
            {
                var (sync, async) = operation switch
                {
                    StateManagerOperation.Read => (hooks.OnRead, hooks.OnReadAsync),
                    StateManagerOperation.Write => (hooks.OnWrite, hooks.OnWriteAsync),
                    StateManagerOperation.Clear => (hooks.OnClear, hooks.OnClearAsync),
                    _ => throw new ArgumentOutOfRangeException(nameof(operation))
                };
                sync?.Invoke(state);
                if (async is not null)
                {
                    await async(state, cancellationToken);
                }
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }
        finally
        {
            invocationDepth.Value--;
        }

        if (errors is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        }
        if (errors is { Count: > 1 })
        {
            throw new AggregateException(errors);
        }
    }
}
