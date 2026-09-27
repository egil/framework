using System.Runtime.CompilerServices;
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
    /// Handlers require only the payload. Delivery token, grain factory, and
    /// cancellation token are independently optional, in that relative order.
    /// On C# 13+, two-argument handlers prefer delivery token, then grain factory,
    /// then cancellation. Three-argument handlers prefer token/cancellation,
    /// then token/factory, then factory/cancellation. Within each shape,
    /// ValueTask is preferred over Task for ordinary async lambdas.
    /// Only applicable overloads participate; explicitly typed handlers can select
    /// any shape. Older compilers may require explicit parameter and return types.
    /// </remarks>
    // Shape priority preserves existing token-based callbacks when lambda parameters
    // are unused or accept multiple types. Adjacent priorities prefer ValueTask within
    // each shape without letting a different shape win just because of its return type.
    // C# filters applicability before priority; typed Task handlers remain callable.
    // https://github.com/dotnet/csharplang/blob/main/proposals/csharp-13.0/overload-resolution-priority.md
    [OverloadResolutionPriority(1)]
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, ValueTask> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        AddPayloadPostman<TSub>((message, _) => postman(message));
        return this;
    }

    /// <summary>Registers a payload handler that also receives the stable delivery token.</summary>
    [OverloadResolutionPriority(5)]
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, OutboxSequenceToken, ValueTask> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>((message, token, _) => postman(message, token));
    }

    /// <summary>Registers a payload handler that receives the grain factory for resolving destinations.</summary>
    [OverloadResolutionPriority(3)]
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, IGrainFactory, ValueTask> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        AddPayloadPostman<TSub>((message, _) => postman(message, grainFactory));
        return this;
    }

    /// <summary>Registers a cancellable payload handler.</summary>
    [OverloadResolutionPriority(1)]
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, CancellationToken, ValueTask> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        AddPayloadPostman(postman);
        return this;
    }

    /// <summary>Registers a payload handler with its delivery token and the grain factory.</summary>
    [OverloadResolutionPriority(3)]
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, OutboxSequenceToken, IGrainFactory, ValueTask> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>((message, token, _) => postman(message, token, grainFactory));
    }

    /// <summary>Registers a cancellable payload handler with its stable delivery token.</summary>
    [OverloadResolutionPriority(5)]
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, OutboxSequenceToken, CancellationToken, ValueTask> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        postmen.Add(typeof(TSub), item => item.Message is TSub,
            (item, cancellationToken) => postman((TSub)item.Message,
                item.Id.ForSender(owner.GrainContext.GrainId), cancellationToken));
        return this;
    }

    /// <summary>Registers a cancellable payload handler with the grain factory.</summary>
    [OverloadResolutionPriority(1)]
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, IGrainFactory, CancellationToken, ValueTask> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        AddPayloadPostman<TSub>((message, cancellationToken) => postman(message, grainFactory, cancellationToken));
        return this;
    }

    /// <summary>Registers a cancellable payload handler with its delivery token and the grain factory.</summary>
    [OverloadResolutionPriority(1)]
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, OutboxSequenceToken, IGrainFactory, CancellationToken, ValueTask> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>((message, token, cancellationToken) =>
            postman(message, token, grainFactory, cancellationToken));
    }

    /// <summary>Registers a Task payload handler.</summary>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, Task> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>(message => new ValueTask(postman(message)));
    }

    /// <summary>Registers a Task payload handler that also receives the stable delivery token.</summary>
    [OverloadResolutionPriority(4)]
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, OutboxSequenceToken, Task> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>((message, token) => new ValueTask(postman(message, token)));
    }

    /// <summary>Registers a Task payload handler that receives the grain factory for resolving destinations.</summary>
    [OverloadResolutionPriority(2)]
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, IGrainFactory, Task> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>((message, grains) => new ValueTask(postman(message, grains)));
    }

    /// <summary>Registers a cancellable Task payload handler.</summary>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, CancellationToken, Task> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>((message, cancellationToken) => new ValueTask(postman(message, cancellationToken)));
    }

    /// <summary>Registers a Task payload handler with its delivery token and the grain factory.</summary>
    [OverloadResolutionPriority(2)]
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, OutboxSequenceToken, IGrainFactory, Task> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>((message, token, grains) => new ValueTask(postman(message, token, grains)));
    }

    /// <summary>Registers a cancellable Task payload handler with its stable delivery token.</summary>
    [OverloadResolutionPriority(4)]
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, OutboxSequenceToken, CancellationToken, Task> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>((message, token, cancellationToken) => new ValueTask(postman(message, token, cancellationToken)));
    }

    /// <summary>Registers a cancellable Task payload handler with the grain factory.</summary>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, IGrainFactory, CancellationToken, Task> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>((message, grains, cancellationToken) => new ValueTask(postman(message, grains, cancellationToken)));
    }

    /// <summary>Registers a cancellable Task payload handler with its delivery token and the grain factory.</summary>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, OutboxSequenceToken, IGrainFactory, CancellationToken, Task> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>((message, token, grains, cancellationToken) => new ValueTask(postman(message, token, grains, cancellationToken)));
    }

    /// <summary>Selects an existing stream provider for a group of postman registrations.</summary>
    /// <remarks>
    /// This does not install an Orleans provider or create another processor. Each
    /// nested registration immediately joins this processor's first-match registry.
    /// </remarks>
    public OutboxStreamProviderBuilder<TOutbox> ForStreamProvider(string streamProviderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamProviderName);
        return new(this, streamProviderName);
    }

    /// <summary>Configures stream postmen for a provider and returns this processor for further registrations.</summary>
    /// <remarks>
    /// Configuration runs synchronously. Registrations take effect immediately in callback order;
    /// if configuration throws, registrations already made remain on this processor.
    /// </remarks>
    public OutboxProcessor<TOutbox> ForStreamProvider(
        string streamProviderName, Action<OutboxStreamProviderBuilder<TOutbox>> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamProviderName);
        ArgumentNullException.ThrowIfNull(configure);
        configure(ForStreamProvider(streamProviderName));
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

    /// <summary>Publishes the original payload to a stream selected using its delivery token.</summary>
    public OutboxProcessor<TOutbox> AddStreamPostman<TSub>(
        string streamProviderName,
        Func<TSub, OutboxSequenceToken, StreamId> streamId)
        where TSub : TOutbox =>
        AddStreamPostman<TSub, TSub>(streamProviderName, streamId, static message => message);

    /// <summary>Uses the delivery token for routing while projecting only the payload.</summary>
    public OutboxProcessor<TOutbox> AddStreamPostman<TSub, TEvent>(
        string streamProviderName,
        Func<TSub, OutboxSequenceToken, StreamId> streamId,
        Func<TSub, TEvent> project)
        where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(project);
        // Routing metadata and the event contract are independent. Reuse the token-aware
        // delivery path without requiring callers to add an unused projection parameter.
        return AddStreamPostman<TSub, TEvent>(streamProviderName, streamId, (message, _) => project(message));
    }

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

    // No OverloadResolutionPriority here. It only applies once type arguments are omitted,
    // and there it would prune the better candidate: a projection returning a type derived
    // from TSub infers the derived TEvent today and must keep publishing to that stream type.
    /// <summary>Enriches each payload from its delivery token before publishing it to the selected stream.</summary>
    public OutboxProcessor<TOutbox> AddStreamPostman<TSub>(
        string streamProviderName,
        Func<TSub, StreamId> streamId,
        Func<TSub, OutboxSequenceToken, TSub> project)
        where TSub : TOutbox =>
        AddStreamPostman<TSub, TSub>(streamProviderName, streamId, project);

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

    /// <summary>Selects the stream and enriches the payload, both using the delivery token.</summary>
    public OutboxProcessor<TOutbox> AddStreamPostman<TSub>(
        string streamProviderName,
        Func<TSub, OutboxSequenceToken, StreamId> streamId,
        Func<TSub, OutboxSequenceToken, TSub> project)
        where TSub : TOutbox =>
        AddStreamPostman<TSub, TSub>(streamProviderName, streamId, project);

    private void AddPayloadPostman<TSub>(Func<TSub, CancellationToken, ValueTask> postman)
        where TSub : TOutbox =>
        postmen.Add(typeof(TSub), item => item.Message is TSub,
            (item, cancellationToken) => postman((TSub)item.Message, cancellationToken));
}
