# Egil.Orleans.Messaging

Composable messaging infrastructure for Microsoft Orleans grains.

`Egil.Orleans.Messaging` provides building blocks for grains that need durable state changes and durable message handoff to move together:

- `IStateManager<T>` wraps `IPersistentState<T>` so a grain does not keep observing uncommitted state after ambiguous write failures.
- `Outbox<T>` stores messages alongside grain state and assigns durable sender sequence tokens.
- `OutboxProcessor<T>` dispatches pending outbox items through registered postmen, with retry, reminder forwarding, failure reconciliation, and telemetry.
- `MessageTracker` records receiver-side high-water marks for outbox messages and Orleans streams.
- `StreamManager` gives grains a fluent subscription facade with resume-token and handler-error support.

## Install

```shell
dotnet add package Egil.Orleans.Messaging
```

Provider-specific integrations are shipped as companion packages:

```shell
dotnet add package Egil.Orleans.Messaging.Streams.EventHubs
dotnet add package Egil.Orleans.Messaging.State.AzureStorage
```

Use the capability namespaces for the tools you need:

```csharp
using Egil.Orleans.Messaging.Outboxes;
using Egil.Orleans.Messaging.State;
using Egil.Orleans.Messaging.Streams;
using Egil.Orleans.Messaging.Tracking;
```

Registration extension members live with the Orleans, hosting, and DI types
they extend:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
```

## State Manager

Register the default state manager factory on the silo:

```csharp
siloBuilder.AddDefaultStateManager("state");
```

For Orleans Azure Table or Blob grain storage, install
`Egil.Orleans.Messaging.State.AzureStorage` and register the Azure-aware
factory instead:

```csharp
siloBuilder.AddAzureStorageStateManager("state");
```

The Azure-aware manager uses Azure SDK `RequestFailedException.Status` and
`ErrorCode` values to decide recovery. Optimistic-concurrency and rejected
request failures such as HTTP 412, 409, 404, authentication/authorization
failures, and payload/validation failures are treated as definite
non-persistence, so writes and clears fail fast without an unnecessary
recovery read. Ambiguous or transient outcomes, including HTTP 503
`ServerBusy`, HTTP 500 `OperationTimedOut`, HTTP 429 throttling, no-response
failures, and timeout exceptions, still use read-back recovery.

Then wrap the Orleans persistent state facet during activation:

```csharp
public sealed class OrderGrain(
    [PersistentState("state", "Default")] IPersistentState<OrderState> storage)
    : Grain, IOrderGrain
{
    private IStateManager<OrderState> state = default!;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        state = this.RegisterStateManager("state", storage);
        return Task.CompletedTask;
    }

    public async Task RenameAsync(string name)
    {
        await state.WriteAsync(state.State with { Name = name });
    }
}
```

State types must be reference types and implement `IEquatable<T>`. For non-trivial state graphs, inherit from `VersionedState` so the recovery path compares a library-stamped version rather than relying on structural collection equality.

## Outbox

Store an `Outbox<T>` on the grain state and commit messages with the business state change:

```csharp
[GenerateSerializer]
public sealed record OrderState : VersionedState
{
    [Id(0)] public string? Name { get; init; }

    [Id(1)] public Outbox<IOrderEvent> Outbox { get; init; } =
        Outbox<IOrderEvent>.Create(GrainId.Create("order", "example"));
}

public async Task SubmitAsync()
{
    var next = state.State with
    {
        Outbox = state.State.Outbox.Add(new OrderSubmitted())
    };

    await state.WriteAsync(next);
    await outboxProcessor.PostInBackgroundAsync();
}
```

Use `OutboxProcessor<T>` to dispatch pending items and acknowledge only the items that were posted successfully:

```csharp
public sealed class OrderGrain : Grain, IOutboxGrain
{
    private OutboxProcessor<IOrderEvent> outboxProcessor = default!;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        outboxProcessor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<IOrderEvent>
        {
            PendingItems = () => state.State.Outbox.Select(item => item.Message).ToImmutableArray(),
            AcknowledgePostedAsync = async (items, ct) =>
            {
                var posted = state.State.Outbox
                    .Take(items.Length)
                    .Select(item => item.Token);

                await state.WriteAsync(state.State with
                {
                    Outbox = state.State.Outbox.RemoveRange(posted)
                });
            },
            ReconcileFailedAsync = (_, _) => ValueTask.CompletedTask,
        })
        .AddPostman<OrderSubmitted>(PublishSubmittedAsync);

        return Task.CompletedTask;
    }
}
```

`IOutboxGrain` forwards reminder ticks to the attached processor. The grain remains responsible for its own message contracts, posting target, and dead-letter policy.
Postman matching is first-match-wins: register specific message types before
base interfaces or catch-all handlers.

Failed dispatches are reported through `ReconcileFailedAsync`. That callback
is where the owning grain applies retry, dead-letter, max-depth, or trimming
policy, because the grain owns the durable outbox state.

If a post run fails before reconciliation completes — for example when the
run exceeds `ProcessingTimeout` or an acknowledgement callback throws — the
processor arms its retry timer and durable reminder before rethrowing, so
pending items are retried without requiring another explicit post. Successful
posts never pay reminder I/O: `PostInBackgroundAsync` schedules an in-memory
grain timer only, and the durable reminder is registered lazily when a run
fails or leaves items pending.

Background outbox postage allows unrelated grain calls to continue while
postmen await I/O by default. `IPostman<T>` services should be state-free with
respect to the owning grain. Inline lambda postmen may read activation-local
state, but should not write it; durable changes belong in
`AcknowledgePostedAsync` or `ReconcileFailedAsync`.
Postmen run on Orleans' activation scheduler, not on the .NET thread pool.
Acknowledgement and failure callbacks are non-interleaving by default, so they
do not interleave with normal grain calls unless
`InterleaveReconciliationCallbacks` is enabled. Reentrant grains can still
interleave according to Orleans' normal scheduling rules.
Pending items in a post run are dispatched concurrently. Successful items are
still acknowledged as one ordered batch after all dispatches complete, and
failed items are reconciled as one batch.

For reusable delivery code, implement and register keyed postman services:

```csharp
[OutboxPostman("orders")]
public sealed class OrderEventPostman : IPostman<OrderSubmitted>
{
    public async ValueTask PostAsync(OrderSubmitted message, CancellationToken ct)
    {
        await publisher.PublishAsync(message, ct);
    }
}

services.AddOutboxPostman<OrderEventPostman>();
```

Then resolve the postman by name from the grain activation service provider:

```csharp
outboxProcessor = this.RegisterOutboxProcessor(options)
    .AddPostman<OrderSubmitted>("orders");
```

For common Orleans targets, use the built-in helpers instead of writing the
callback by hand:

```csharp
outboxProcessor = this.RegisterOutboxProcessor(options)
    .AddStreamPostman<OrderSubmitted>(
        "order-streams",
        message => StreamId.Create("submitted-orders", message.OrderId));
```

```csharp
outboxProcessor = this.RegisterOutboxProcessor(options)
    .AddGrainPostman<OrderSubmitted, IOrderProjectionGrain>(
        (message, grainFactory) => grainFactory.GetGrain<IOrderProjectionGrain>(message.OrderId),
        (grain, message) => grain.ApplyAsync(message));
```

## Receiver Dedup

`MessageTracker` accepts a message only when its stream token, stream cursor, or outbox token advances the stored high-water mark:

```csharp
if (!state.State.Tracker.ProcessMessage("prices", token, out var tracker))
{
    return;
}

await state.WriteAsync(state.State with { Tracker = tracker });
```

Use `LatestStreamSequenceToken("prices")` when all you need is the previous
resume token. Keep using `LatestStream("prices")` when you need the full
cursor or must distinguish "no stream tracked" from "tracked stream with a
null token".

The tracker can also evict old sender or stream entries when your retention policy allows it.

## Streams

Use `StreamManager` to configure stream subscriptions from `OnActivateAsync`. Pass a tracker snapshot when you want persisted resume tokens, or omit it when the grain does not track stream positions:

```csharp
streamManager = this.RegisterStreamManager(state.State.Tracker)
    .ConfigureExplicitSubscription<PriceChanged>(
        "StreamProvider",
        "prices",
        async (message, cursor) =>
        {
            if (!state.State.Tracker.ProcessMessage(cursor, out var tracker))
            {
                return;
            }

            await state.WriteAsync(state.State with { Tracker = tracker });
        });

await streamManager.EnsureExplicitSubscriptionsAsync(cancellationToken);
```

The string namespace overload derives a stream id from the receiving grain
identity. Use the `StreamId` overload for an explicit stream keyed by another
application id:

```csharp
streamManager = this.RegisterStreamManager(state.State.Tracker)
    .ConfigureExplicitSubscription<PriceChanged>(
        "StreamProvider",
        StreamId.Create("prices", customerId),
        HandlePriceChangedAsync);
```

Tracked resume tokens are a per-subscription choice. The default is to pass
the previous token when a tracker snapshot is supplied. Opt out when a
subscription should attach without a resume token:

```csharp
streamManager = this.RegisterStreamManager(state.State.Tracker)
    .ConfigureExplicitSubscription<PriceChanged>(
        "StreamProvider",
        "prices",
        HandlePriceChangedAsync,
        useTrackedResumeToken: false);
```

```csharp
this.RegisterStreamManager()
    .ConfigureImplicitSubscription<PriceChanged>(
        "prices",
        async (message, cursor) => await UpdateProjectionAsync(message));
```

Install `Egil.Orleans.Messaging.Streams.EventHubs` when using Orleans Event
Hubs streams and the enriched adapter/token support:

```csharp
using Egil.Orleans.Messaging.Streams.EventHubs;
using Orleans.Hosting;
```

Registering the enriched adapter also registers Event Hubs sequence-token JSON
converters, so `MessageTracker` and `StreamCursor` can persist and restore
`EnrichedEventHubSequenceToken` without downcasting it to the Orleans base
event token:

```csharp
siloBuilder.AddEventHubStreams("event-hubs", configurator =>
{
    configurator.UseEnrichedDataAdapter();
});
```

The core package can consume provider-specific token metadata through
`IStreamSequenceTokenMetadata` without taking a direct Event Hubs dependency.
Custom stream providers that expose custom `StreamSequenceToken` types should
register a `JsonConverter<TToken>` with `StreamSequenceTokenJsonConverters`
during startup.

## Scope

This package is messaging infrastructure, not an event-sourcing or CQRS framework. It wraps Orleans state, outbox dispatch, receiver deduplication, and stream subscription management while leaving domain modeling, read models, transport targets, and operational policy to the application.
