namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Represents the persisted projection state in an immutable structure.
/// </summary>
/// <typeparam name="TProjection">The type of the projection</typeparam>
/// <param name="Projection">The projection data</param>
/// <param name="LastAppliedSequenceNumber">The sequence number of the last applied event</param>
/// <param name="Version">The version of the projection schema</param>
public sealed record ProjectionState<TProjection>(
    TProjection? Projection,
    long LastAppliedSequenceNumber,
    int Version = 1)
    where TProjection : notnull;

/// <summary>
/// Result of processing an event, containing the updated projection and outbox.
/// </summary>
/// <typeparam name="TProjection">The type of the projection</typeparam>
/// <param name="Projection">The updated projection</param>
/// <param name="Outbox">The outbox events to be published</param>
/// <param name="SequenceNumber">The sequence number of the processed event</param>
public sealed record EventProcessingResult<TProjection>(
    TProjection Projection,
    Outbox Outbox,
    long SequenceNumber)
    where TProjection : notnull;
