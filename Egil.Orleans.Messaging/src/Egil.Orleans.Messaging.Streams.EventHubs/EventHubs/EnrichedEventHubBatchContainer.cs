using Orleans.Streams;

namespace Egil.Orleans.Messaging.Streams.EventHubs;

/// <summary>
/// Preserves the base Event Hubs batch payload and request-context behavior
/// while exposing enriched sequence tokens to stream consumers.
/// </summary>
[GenerateSerializer]
[Alias("egil.orleans.messaging.EnrichedEventHubBatchContainer")]
internal sealed class EnrichedEventHubBatchContainer : IBatchContainer
{
    [Id(0)]
    private IBatchContainer inner = default!;

    [Id(1)]
    private EnrichedEventHubSequenceToken sequenceToken = default!;

    public EnrichedEventHubBatchContainer(
        IBatchContainer inner,
        EnrichedEventHubSequenceToken sequenceToken)
    {
        this.inner = inner;
        this.sequenceToken = sequenceToken;
    }

    /// <summary>
    /// Creates an instance for Orleans serialization.
    /// </summary>
    public EnrichedEventHubBatchContainer()
    {
    }

    /// <inheritdoc/>
    public StreamId StreamId => inner.StreamId;

    /// <inheritdoc/>
    public StreamSequenceToken SequenceToken => sequenceToken;

    /// <inheritdoc/>
    /// <remarks>
    /// A custom inner container supplied through
    /// <c>EnrichedEventHubAdapter.CreateInnerBatchContainer</c> may yield
    /// <c>null</c> per-event tokens, since the adapter replaces them anyway.
    /// Enumeration order then supplies the event index, which matches what the
    /// base Event Hubs container produces for the same batch.
    /// </remarks>
    public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>()
    {
        var index = 0;
        foreach (var item in inner.GetEvents<T>())
        {
            yield return Tuple.Create(
                item.Item1,
                (StreamSequenceToken)new EnrichedEventHubSequenceToken(
                    sequenceToken.EventHubOffset,
                    sequenceToken.SequenceNumber,
                    item.Item2?.EventIndex ?? index,
                    sequenceToken.EnqueuedTime,
                    sequenceToken.ProviderName,
                    sequenceToken.TraceParent));
            index++;
        }
    }

    /// <inheritdoc/>
    public bool ImportRequestContext() => inner.ImportRequestContext();
}
