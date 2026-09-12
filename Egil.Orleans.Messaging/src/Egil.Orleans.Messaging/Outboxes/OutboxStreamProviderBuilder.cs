namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>Groups stream postmen that use the same configured Orleans provider.</summary>
/// <remarks>
/// This builder stores only registration context. It forwards every call immediately
/// to the original processor, preserving registration order across grouped and direct
/// postmen. Delivery, retry, and acknowledgment remain owned by that processor.
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

    /// <summary>Selects streams and projects payloads using their delivery tokens.</summary>
    public OutboxStreamProviderBuilder<TOutbox> AddStreamPostman<TSub, TEvent>(
        Func<TSub, OutboxSequenceToken, StreamId> streamId,
        Func<TSub, OutboxSequenceToken, TEvent> project)
        where TSub : TOutbox
    {
        processor.AddStreamPostman(streamProviderName, streamId, project);
        return this;
    }
}
