namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>Groups stream postmen that use the same configured Orleans provider.</summary>
/// <remarks>
/// This builder stores only registration context. It forwards every call immediately
/// to the original processor, preserving registration order across grouped and direct
/// postmen. Delivery, retry, and acknowledgement remain owned by that processor.
/// </remarks>
/// <typeparam name="TOutbox">The processor's base payload type.</typeparam>
public sealed class OutboxStreamProviderBuilder<TOutbox> where TOutbox : notnull
{
    private readonly OutboxProcessor<TOutbox> processor;
    private readonly string streamProviderName;

    internal OutboxStreamProviderBuilder(OutboxProcessor<TOutbox> processor, string streamProviderName)
    {
        this.processor = processor;
        this.streamProviderName = streamProviderName;
    }

    /// <summary>Publishes matching payloads to a supplied collection of streams.</summary>
    /// <remarks>
    /// The collection is retained without copying and enumerated per attempt, including repeated IDs.
    /// Supply a repeatable sequence. An empty collection is a successful no-op.
    /// Delivery completes only after every publication succeeds; a retry may publish again
    /// to destinations that already succeeded, retaining the same outbox token.
    /// </remarks>
    public OutboxStreamProviderBuilder<TOutbox> AddStreamPostman<TSub>(IEnumerable<StreamId> streamIds)
        where TSub : TOutbox
    {
        processor.AddStreamPostman<TSub>(streamProviderName, streamIds);
        return this;
    }

    /// <summary>Publishes matching payloads to a collection of streams selected for each delivery attempt.</summary>
    /// <remarks>
    /// The result is enumerated while publishing, including repeated IDs.
    /// Empty results are successful no-ops; null results fail delivery. A failure retries
    /// selection and all publications using the same outbox token, including after an enumeration failure.
    /// </remarks>
    public OutboxStreamProviderBuilder<TOutbox> AddStreamPostman<TSub>(Func<TSub, IEnumerable<StreamId>> streamIds)
        where TSub : TOutbox
    {
        processor.AddStreamPostman<TSub>(streamProviderName, streamIds);
        return this;
    }

    /// <summary>Selects multiple streams using the message and its delivery token.</summary>
    /// <remarks>
    /// The selector runs once per attempt and is enumerated while publishing, including repeated IDs.
    /// An empty result is a successful no-op and null fails delivery.
    /// Every destination receives the same outbox token, including on retries after partial failure.
    /// </remarks>
    public OutboxStreamProviderBuilder<TOutbox> AddStreamPostman<TSub>(
        Func<TSub, OutboxSequenceToken, IEnumerable<StreamId>> streamIds)
        where TSub : TOutbox
    {
        processor.AddStreamPostman<TSub>(streamProviderName, streamIds);
        return this;
    }

    /// <summary>Publishes matching payloads to their selected streams.</summary>
    public OutboxStreamProviderBuilder<TOutbox> AddStreamPostman<TSub>(Func<TSub, StreamId> streamId)
        where TSub : TOutbox
    {
        processor.AddStreamPostman(streamProviderName, streamId);
        return this;
    }

    /// <summary>Publishes original payloads to streams selected using their delivery tokens.</summary>
    public OutboxStreamProviderBuilder<TOutbox> AddStreamPostman<TSub>(
        Func<TSub, OutboxSequenceToken, StreamId> streamId)
        where TSub : TOutbox
    {
        processor.AddStreamPostman(streamProviderName, streamId);
        return this;
    }

    /// <summary>Uses delivery tokens for routing while projecting only the payload.</summary>
    public OutboxStreamProviderBuilder<TOutbox> AddStreamPostman<TSub, TEvent>(
        Func<TSub, OutboxSequenceToken, StreamId> streamId, Func<TSub, TEvent> project)
        where TSub : TOutbox
    {
        processor.AddStreamPostman(streamProviderName, streamId, project);
        return this;
    }

    /// <summary>Projects matching payloads and publishes them to their selected streams.</summary>
    public OutboxStreamProviderBuilder<TOutbox> AddStreamPostman<TSub, TEvent>(
        Func<TSub, StreamId> streamId, Func<TSub, TEvent> project)
        where TSub : TOutbox
    {
        processor.AddStreamPostman(streamProviderName, streamId, project);
        return this;
    }

    /// <summary>Projects payloads with their delivery tokens before publishing them.</summary>
    public OutboxStreamProviderBuilder<TOutbox> AddStreamPostman<TSub, TEvent>(
        Func<TSub, StreamId> streamId, Func<TSub, OutboxSequenceToken, TEvent> project)
        where TSub : TOutbox
    {
        processor.AddStreamPostman(streamProviderName, streamId, project);
        return this;
    }

    // The overloads above let the compiler infer the type arguments they forward with. This
    // one names them, because an inferred call would match the processor's two-type-parameter
    // projection overload and its enriching overload equally well, and fail as ambiguous.
    /// <summary>Enriches each payload from its delivery token before publishing it to the selected stream.</summary>
    public OutboxStreamProviderBuilder<TOutbox> AddStreamPostman<TSub>(
        Func<TSub, StreamId> streamId, Func<TSub, OutboxSequenceToken, TSub> project)
        where TSub : TOutbox
    {
        processor.AddStreamPostman<TSub, TSub>(streamProviderName, streamId, project);
        return this;
    }

    /// <summary>Selects streams and projects payloads using their delivery tokens.</summary>
    public OutboxStreamProviderBuilder<TOutbox> AddStreamPostman<TSub, TEvent>(
        Func<TSub, OutboxSequenceToken, StreamId> streamId,
        Func<TSub, OutboxSequenceToken, TEvent> project)
        where TSub : TOutbox
    {
        processor.AddStreamPostman(streamProviderName, streamId, project);
        return this;
    }

    /// <summary>Selects the stream and enriches the payload, both using the delivery token.</summary>
    public OutboxStreamProviderBuilder<TOutbox> AddStreamPostman<TSub>(
        Func<TSub, OutboxSequenceToken, StreamId> streamId,
        Func<TSub, OutboxSequenceToken, TSub> project)
        where TSub : TOutbox
    {
        processor.AddStreamPostman<TSub, TSub>(streamProviderName, streamId, project);
        return this;
    }
}
