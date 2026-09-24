namespace Egil.Orleans.Messaging.State;

/// <summary>The confirmed storage outcome reported to a lifecycle handler.</summary>
public enum StateManagerOperation
{
    /// <summary>A record or an absent-record default was loaded.</summary>
    Read,
    /// <summary>A write was confirmed.</summary>
    Write,
    /// <summary>A deletion was confirmed.</summary>
    Clear
}

/// <summary>
/// Lifecycle callbacks for one manager. Treat callback state as read-only.
/// Configure at most one synchronous or asynchronous callback per slot.
/// </summary>
public sealed record StateManagerHooks<T> where T : class, IEquatable<T>
{
    /// <summary>Runs after a read adopts state.</summary>
    public Action<T>? OnRead { get; init; }
    /// <summary>Runs and is awaited after a read adopts state.</summary>
    public Func<T, CancellationToken, Task>? OnReadAsync { get; init; }
    /// <summary>Runs after a confirmed write.</summary>
    public Action<T>? OnWrite { get; init; }
    /// <summary>Runs and is awaited after a confirmed write.</summary>
    public Func<T, CancellationToken, Task>? OnWriteAsync { get; init; }
    /// <summary>Receives the fresh default after a confirmed clear.</summary>
    public Action<T>? OnClear { get; init; }
    /// <summary>Receives the fresh default and is awaited after a confirmed clear.</summary>
    public Func<T, CancellationToken, Task>? OnClearAsync { get; init; }
    /// <summary>Runs before the specific handler, with the confirmed record existence.</summary>
    public Action<T, StateManagerOperation, bool>? OnChange { get; init; }
    /// <summary>Runs and is awaited before the specific handler.</summary>
    public Func<T, StateManagerOperation, bool, CancellationToken, Task>? OnChangeAsync { get; init; }

    internal void Validate()
    {
        if ((OnRead is not null && OnReadAsync is not null)
            || (OnWrite is not null && OnWriteAsync is not null)
            || (OnClear is not null && OnClearAsync is not null)
            || (OnChange is not null && OnChangeAsync is not null))
        {
            throw new ArgumentException("Each hook slot accepts either a synchronous or an asynchronous handler, not both.");
        }
    }
}
