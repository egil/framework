using Microsoft.Extensions.DependencyInjection;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Outboxes;

public sealed partial class OutboxProcessor<TOutbox>
{
    /// <summary>
    /// Registers a postman that handles payloads of type <typeparamref name="TSub"/>.
    /// </summary>
    /// <remarks>
    /// Postman matching is first-match-wins, like a switch statement. Register
    /// more specific message types before base interfaces or catch-all types.
    /// Each outbox item is dispatched to at most one postman.
    /// </remarks>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, ValueTask> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        AddPayloadPostman<TSub>((item, _) => postman(item));
        return this;
    }

    /// <inheritdoc cref="AddPostman{TSub}(Func{TSub, ValueTask})"/>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, Task> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        AddPayloadPostman<TSub>((item, _) => new ValueTask(postman(item)));
        return this;
    }

    /// <inheritdoc cref="AddPostman{TSub}(Func{TSub, ValueTask})"/>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, CancellationToken, Task> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        AddPayloadPostman<TSub>((item, cancellationToken) => new ValueTask(postman(item, cancellationToken)));
        return this;
    }

    /// <inheritdoc cref="AddPostman{TSub}(Func{TSub, ValueTask})"/>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, IGrainFactory, CancellationToken, ValueTask> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        AddPayloadPostman<TSub>((item, cancellationToken) => postman(item, grainFactory, cancellationToken));
        return this;
    }

    /// <summary>
    /// Registers a keyed <see cref="IPostman{TMessage}"/> service that handles
    /// items of type <typeparamref name="TSub"/>.
    /// </summary>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(string postmanName)
        where TSub : TOutbox
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postmanName);

        var postman = owner.GrainContext.ActivationServices
            .GetRequiredKeyedService<IPostman<TSub>>(postmanName);

        AddPayloadPostman<TSub>(postman.PostAsync);
        return this;
    }

    /// <summary>
    /// Registers a postman that publishes each item to an Orleans stream.
    /// </summary>
    public OutboxProcessor<TOutbox> AddStreamPostman<TSub>(
        string streamProviderName,
        Func<TSub, StreamId> streamId)
        where TSub : TOutbox =>
        AddStreamPostman<TSub, TSub>(
            streamProviderName,
            streamId,
            static message => message);

    /// <summary>
    /// Registers a postman that projects each item and publishes the projected
    /// event to an Orleans stream.
    /// </summary>
    public OutboxProcessor<TOutbox> AddStreamPostman<TSub, TEvent>(
        string streamProviderName,
        Func<TSub, StreamId> streamId,
        Func<TSub, TEvent> project)
        where TSub : TOutbox
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamProviderName);
        ArgumentNullException.ThrowIfNull(streamId);
        ArgumentNullException.ThrowIfNull(project);

        var streamProvider = owner.GrainContext.ActivationServices
            .GetRequiredKeyedService<IStreamProvider>(streamProviderName);

        AddPayloadPostman<TSub>((message, _) =>
        {
            var stream = streamProvider.GetStream<TEvent>(streamId(message));
            return new ValueTask(stream.OnNextAsync(project(message)));
        });
        return this;
    }

    /// <summary>
    /// Registers a postman that resolves a grain for each item and invokes it.
    /// </summary>
    public OutboxProcessor<TOutbox> AddGrainPostman<TSub, TGrain>(
        Func<TSub, IGrainFactory, TGrain> resolveGrain,
        Func<TGrain, TSub, Task> call)
        where TSub : TOutbox
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(resolveGrain);
        ArgumentNullException.ThrowIfNull(call);

        AddPayloadPostman<TSub>((message, _) => new ValueTask(call(resolveGrain(message, grainFactory), message)));
        return this;
    }

    /// <summary>Registers a payload handler that also receives the stable delivery token.</summary>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(Func<TSub, OutboxSequenceToken, Task> postman)
        where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>((message, token, _) => new ValueTask(postman(message, token)));
    }

    /// <summary>Registers a cancellable payload handler with its stable delivery token.</summary>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(Func<TSub, OutboxSequenceToken, CancellationToken, Task> postman)
        where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>((message, token, cancellationToken) => new ValueTask(postman(message, token, cancellationToken)));
    }

    /// <summary>Registers a cancellable payload handler with its stable delivery token.</summary>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(Func<TSub, OutboxSequenceToken, CancellationToken, ValueTask> postman)
        where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        postmen.Add(typeof(TSub), item => item.Message is TSub,
            (item, cancellationToken) => postman((TSub)item.Message,
                item.Id.ForSender(owner.GrainContext.GrainId), cancellationToken));
        return this;
    }

    /// <summary>Registers a stream projection which can include delivery metadata in its event.</summary>
    public OutboxProcessor<TOutbox> AddStreamPostman<TSub, TEvent>(
        string streamProviderName,
        Func<TSub, StreamId> streamId,
        Func<TSub, OutboxSequenceToken, TEvent> project)
        where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(streamId);
        return AddStreamPostman<TSub, TEvent>(streamProviderName, (message, _) => streamId(message), project);
    }

    /// <summary>Registers token-aware stream selection and projection for a payload.</summary>
    public OutboxProcessor<TOutbox> AddStreamPostman<TSub, TEvent>(
        string streamProviderName,
        Func<TSub, OutboxSequenceToken, StreamId> streamId,
        Func<TSub, OutboxSequenceToken, TEvent> project)
        where TSub : TOutbox
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamProviderName);
        ArgumentNullException.ThrowIfNull(streamId);
        ArgumentNullException.ThrowIfNull(project);
        var streamProvider = owner.GrainContext.ActivationServices
            .GetRequiredKeyedService<IStreamProvider>(streamProviderName);
        return AddPostman<TSub>((message, token, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask(streamProvider.GetStream<TEvent>(streamId(message, token))
                .OnNextAsync(project(message, token)));
        });
    }

    /// <summary>Registers a grain invocation that can forward the delivery token for deduplication.</summary>
    public OutboxProcessor<TOutbox> AddGrainPostman<TSub, TGrain>(
        Func<TSub, IGrainFactory, TGrain> resolveGrain,
        Func<TGrain, TSub, OutboxSequenceToken, Task> call)
        where TSub : TOutbox
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(call);
        return AddGrainPostman<TSub, TGrain>(resolveGrain,
            (grain, message, token, _) => new ValueTask(call(grain, message, token)));
    }

    /// <summary>Registers a cancellable grain invocation with payload and delivery token.</summary>
    public OutboxProcessor<TOutbox> AddGrainPostman<TSub, TGrain>(
        Func<TSub, IGrainFactory, TGrain> resolveGrain,
        Func<TGrain, TSub, OutboxSequenceToken, CancellationToken, ValueTask> call)
        where TSub : TOutbox
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(resolveGrain);
        ArgumentNullException.ThrowIfNull(call);
        return AddPostman<TSub>((message, token, cancellationToken) =>
            call(resolveGrain(message, grainFactory), message, token, cancellationToken));
    }

    private void AddPayloadPostman<TSub>(Func<TSub, CancellationToken, ValueTask> postman)
        where TSub : TOutbox =>
        postmen.Add(typeof(TSub), item => item.Message is TSub,
            (item, cancellationToken) => postman((TSub)item.Message, cancellationToken));
}
