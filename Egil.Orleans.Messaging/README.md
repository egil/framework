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

Use the capability namespaces for the tools you need:

```csharp
using Egil.Orleans.Messaging.Outboxes;
using Egil.Orleans.Messaging.State;
using Egil.Orleans.Messaging.Streams;
using Egil.Orleans.Messaging.Streams.EventHubs;
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

## Receiver Dedup

`MessageTracker` accepts a message only when its stream cursor or outbox token advances the stored high-water mark:

```csharp
if (!state.State.Tracker.ProcessMessage(token, out var tracker))
{
    return;
}

await state.WriteAsync(state.State with { Tracker = tracker });
```

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

```csharp
this.RegisterStreamManager()
    .ConfigureImplicitSubscription<PriceChanged>(
        "prices",
        async (message, cursor) => await UpdateProjectionAsync(message));
```

## Scope

This package is messaging infrastructure, not an event-sourcing or CQRS framework. It wraps Orleans state, outbox dispatch, receiver deduplication, and stream subscription management while leaving domain modeling, read models, transport targets, and operational policy to the application.
