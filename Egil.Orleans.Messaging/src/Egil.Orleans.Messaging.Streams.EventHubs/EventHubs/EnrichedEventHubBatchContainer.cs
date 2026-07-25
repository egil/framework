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
    public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>()
    {
        foreach (var item in inner.GetEvents<T>())
        {
            yield return Tuple.Create(
                item.Item1,
                (StreamSequenceToken)new EnrichedEventHubSequenceToken(
                    sequenceToken.EventHubOffset,
                    sequenceToken.SequenceNumber,
                    item.Item2.EventIndex,
                    sequenceToken.EnqueuedTime,
                    sequenceToken.ProviderName,
                    sequenceToken.TraceParent));
        }
    }

    /// <inheritdoc/>
    public bool ImportRequestContext() => inner.ImportRequestContext();
}
