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
/// Instances are supplied by ConfigureHooks; the manager copies the callbacks
/// after configuration, so retaining and editing this object cannot change installed hooks.
/// </summary>
public sealed class StateManagerHooks<T> where T : class, IEquatable<T>
{
    internal StateManagerHooks()
    {
    }

    /// <summary>Runs after a read adopts state.</summary>
    public Action<T>? OnRead { get; set; }
    /// <summary>Runs and is awaited after a read adopts state.</summary>
    public Func<T, CancellationToken, Task>? OnReadAsync { get; set; }
    /// <summary>Runs after a confirmed write.</summary>
    public Action<T>? OnWrite { get; set; }
    /// <summary>Runs and is awaited after a confirmed write.</summary>
    public Func<T, CancellationToken, Task>? OnWriteAsync { get; set; }
    /// <summary>Receives the fresh default after a confirmed clear.</summary>
    public Action<T>? OnClear { get; set; }
    /// <summary>Receives the fresh default and is awaited after a confirmed clear.</summary>
    public Func<T, CancellationToken, Task>? OnClearAsync { get; set; }
    /// <summary>Runs before the specific handler, with the confirmed record existence.</summary>
    public Action<T, StateManagerOperation, bool>? OnChange { get; set; }
    /// <summary>Runs and is awaited before the specific handler.</summary>
    public Func<T, StateManagerOperation, bool, CancellationToken, Task>? OnChangeAsync { get; set; }

    internal static StateManagerHooks<T> Create(Action<StateManagerHooks<T>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var hooks = new StateManagerHooks<T>();
        configure(hooks);
        hooks.Validate();
        // Caller code may retain the mutable configuration object. Dispatch must use
        // an owned copy so later edits cannot bypass validation or split an operation.
        var snapshot = new StateManagerHooks<T>();
        hooks.CopyTo(snapshot);
        return snapshot;
    }

    internal void CopyTo(StateManagerHooks<T> target)
    {
        target.OnRead = OnRead;
        target.OnReadAsync = OnReadAsync;
        target.OnWrite = OnWrite;
        target.OnWriteAsync = OnWriteAsync;
        target.OnClear = OnClear;
        target.OnClearAsync = OnClearAsync;
        target.OnChange = OnChange;
        target.OnChangeAsync = OnChangeAsync;
    }

    private void Validate()
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
