namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Represents the persisted projection state.
/// </summary>
/// <typeparam name="TState">The type of the projection state</typeparam>
public sealed class ProjectionState<TState> where TState : class
{
    public TState? State { get; set; }
    public long LastAppliedSequenceNumber { get; set; }
}
