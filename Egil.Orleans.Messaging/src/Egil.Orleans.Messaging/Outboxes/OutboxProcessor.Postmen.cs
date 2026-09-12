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
    /// Callbacks take the payload, optionally followed by its delivery token and
    /// cancellation token. Argument count selects the overload, even for unused
    /// lambda parameters. Task and ValueTask callbacks are both supported. With C# 13
    /// or newer, overload priority prefers ValueTask for ordinary async lambdas,
    /// while Task-returning method groups and expressions use the Task adapters.
    /// Older compilers require an explicitly typed delegate or lambda return type.
    /// </remarks>
    [OverloadResolutionPriority(1)]
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, ValueTask> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        AddPayloadPostman<TSub>((item, _) => postman(item));
        return this;
    }

    /// <summary>Registers a ValueTask payload handler that also receives the stable delivery token.</summary>
    [OverloadResolutionPriority(1)]
    public OutboxProcessor<TOutbox> AddPostman<TSub>(Func<TSub, OutboxSequenceToken, ValueTask> postman)
        where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>((message, token, _) => postman(message, token));
    }

    /// <summary>Registers a cancellable payload handler with its stable delivery token.</summary>
    [OverloadResolutionPriority(1)]
    public OutboxProcessor<TOutbox> AddPostman<TSub>(Func<TSub, OutboxSequenceToken, CancellationToken, ValueTask> postman)
        where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        postmen.Add(typeof(TSub), item => item.Message is TSub,
            (item, cancellationToken) => postman((TSub)item.Message,
                item.Id.ForSender(owner.GrainContext.GrainId), cancellationToken));
        return this;
    }

    // Priority is applied to the ValueTask overloads, not these adapters. C# first
    // filters applicable candidates, so Task method groups still reach these methods.
    // When an async lambda fits both return types, priority selects ValueTask instead.
    // https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/csharp-13.0/overload-resolution-priority
    /// <summary>Registers a Task payload handler.</summary>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(Func<TSub, Task> postman)
        where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>(message => new ValueTask(postman(message)));
    }

    /// <summary>Registers a Task payload handler with its delivery token.</summary>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(Func<TSub, OutboxSequenceToken, Task> postman)
        where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>((message, token) => new ValueTask(postman(message, token)));
    }

    /// <summary>Registers a cancellable Task payload handler with its delivery token.</summary>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(Func<TSub, OutboxSequenceToken, CancellationToken, Task> postman)
        where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostman<TSub>((message, token, cancellationToken) => new ValueTask(postman(message, token, cancellationToken)));
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
    /// <remarks>
    /// Like AddPostman, grain invocation callbacks support Task and ValueTask.
    /// Additional arguments supply the delivery token and then cancellation.
    /// ValueTask overload priority keeps ordinary async lambdas unambiguous on C# 13+;
    /// Task-returning grain methods can also be registered directly.
    /// </remarks>
    [OverloadResolutionPriority(1)]
    public OutboxProcessor<TOutbox> AddGrainPostman<TSub, TGrain>(
        Func<TSub, IGrainFactory, TGrain> resolveGrain,
        Func<TGrain, TSub, ValueTask> call)
        where TSub : TOutbox
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(resolveGrain);
        ArgumentNullException.ThrowIfNull(call);

        AddPayloadPostman<TSub>((message, _) => call(resolveGrain(message, grainFactory), message));
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
    [OverloadResolutionPriority(1)]
    public OutboxProcessor<TOutbox> AddGrainPostman<TSub, TGrain>(
        Func<TSub, IGrainFactory, TGrain> resolveGrain,
        Func<TGrain, TSub, OutboxSequenceToken, ValueTask> call)
        where TSub : TOutbox
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(call);
        return AddGrainPostman<TSub, TGrain>(resolveGrain,
            (grain, message, token, _) => call(grain, message, token));
    }

    /// <summary>Registers a cancellable grain invocation with payload and delivery token.</summary>
    [OverloadResolutionPriority(1)]
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

    /// <summary>Registers a Task grain invocation for each payload.</summary>
    public OutboxProcessor<TOutbox> AddGrainPostman<TSub, TGrain>(
        Func<TSub, IGrainFactory, TGrain> resolveGrain,
        Func<TGrain, TSub, Task> call)
        where TSub : TOutbox
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(call);
        return AddGrainPostman<TSub, TGrain>(resolveGrain,
            (grain, message) => new ValueTask(call(grain, message)));
    }

    /// <summary>Registers a Task grain invocation with its delivery token.</summary>
    public OutboxProcessor<TOutbox> AddGrainPostman<TSub, TGrain>(
        Func<TSub, IGrainFactory, TGrain> resolveGrain,
        Func<TGrain, TSub, OutboxSequenceToken, Task> call)
        where TSub : TOutbox
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(call);
        return AddGrainPostman<TSub, TGrain>(resolveGrain,
            (grain, message, token) => new ValueTask(call(grain, message, token)));
    }

    /// <summary>Registers a cancellable Task grain invocation with its delivery token.</summary>
    public OutboxProcessor<TOutbox> AddGrainPostman<TSub, TGrain>(
        Func<TSub, IGrainFactory, TGrain> resolveGrain,
        Func<TGrain, TSub, OutboxSequenceToken, CancellationToken, Task> call)
        where TSub : TOutbox
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(call);
        return AddGrainPostman<TSub, TGrain>(resolveGrain,
            (grain, message, token, cancellationToken) => new ValueTask(call(grain, message, token, cancellationToken)));
    }

    private void AddPayloadPostman<TSub>(Func<TSub, CancellationToken, ValueTask> postman)
        where TSub : TOutbox =>
        postmen.Add(typeof(TSub), item => item.Message is TSub,
            (item, cancellationToken) => postman((TSub)item.Message, cancellationToken));
}
