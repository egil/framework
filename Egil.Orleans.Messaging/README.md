# Egil.Orleans.Messaging

Composable messaging infrastructure for Microsoft Orleans grains.

`Egil.Orleans.Messaging` provides building blocks for grains that need durable state changes and durable message handoff to move together:

- `IStateManager<T>` wraps `IPersistentState<T>` so a grain does not keep observing uncommitted state after ambiguous write failures.
- `Outbox<T>` stores messages alongside grain state and assigns durable message IDs; processors add sender identity at delivery.
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

For Orleans Azure Table or Blob grain storage, install and configure the
Orleans storage provider separately. The Messaging companion works through
`IPersistentState<T>` and Azure SDK exceptions; it does not select or install
the underlying provider. Install `Egil.Orleans.Messaging.State.AzureStorage`
and register the Azure-aware factory instead:

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

Register the manager in the grain constructor and keep it in a readonly field:

```csharp
public sealed class OrderGrain : Grain, IOrderGrain
{
    private readonly IStateManager<OrderState> state;

    public OrderGrain([PersistentState("state", "Default")] IPersistentState<OrderState> storage)
    {
        state = this.RegisterStateManager("state", storage, () => new OrderState());
    }

    public Task RenameAsync(string name) =>
        state.WriteAsync(state.State with { Name = name });
}
```

The overload without a factory requires `TState : new()`. Constructor registration
returns immediately, but the provider-specific manager and default state are
created only after Orleans has hydrated storage, before `OnActivateAsync`.
Accessing `State` before then throws a lifecycle error. Registration in
`OnActivateAsync` remains supported for callers that do not need readonly fields.

`State` is non-null: activation and `ReadAsync` use the configured default when
no persisted record exists, and a successful `ClearAsync` exposes a fresh default.
The factory takes precedence over a provider-created default for a missing record.
An existing record with a null value, or a factory returning null, is rejected.
**Creating a default does not write it to storage.** The first business operation
can persist its resulting state with `WriteAsync`.

`State` stays read-only. It exposes the loaded or successfully written snapshot,
or the default representing absent storage; interleaved readers cannot observe
an in-flight write candidate. Do not replace raw `storage.State` after registration.

Use the optional runtime configuration callback to restore transient dependencies
on each adopted instance, including after reads and recovery:

```csharp
state = this.RegisterStateManager("state", storage,
    () => new OrderState(),
    loaded => loaded.Tracker.RegisterTimeProvider(timeProvider));
```

This callback configures runtime dependencies; it must not change business data
or perform storage I/O. It is deferred during constructor registration. The manager
and raw storage facet expose the adopted snapshot before the callback runs. If the
callback fails, its exception reaches the caller and the adopted snapshot remains
visible. A successful storage operation is not retried because configuration failed.
After a successful recovery read, invalid state and factory/configuration failures
are reported directly; only a failed storage read preserves the original storage error.


`ClearAsync` deletes storage before calling the default-state factory. The factory
must return a valid, non-null state. If it throws or returns null, the error
propagates and the deletion remains completed. A null result is rejected with
`InvalidOperationException` as a diagnostic. After this factory contract violation,
the manager has no guaranteed usable state: `State` may still reference the previous
snapshot and must not be treated as the current persisted state.

State types must be reference types and implement `IEquatable<T>`. For
non-trivial state graphs, inherit from `VersionedState` so the recovery path
compares a library-stamped version rather than relying on structural
collection equality.

## Outbox

Store an `Outbox<T>` on the grain state and commit messages with the business state change:

```csharp
[GenerateSerializer]
public sealed record OrderState : VersionedState
{
    [Id(0)] public string? Name { get; init; }

    [Id(1)] public Outbox<IOrderEvent> Outbox { get; init; } = [];
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

`Outbox<T>` implements `IReadOnlyList<T>`: indexing, enumeration, LINQ, and
collection expressions use payloads. `Envelopes` exposes the same snapshot as an
`ImmutableArray<OutboxMessageEnvelope<T>>`, including the assigned message IDs,
without copying. Collection structure is immutable; do not mutate payload objects
after enqueueing.

```csharp
Outbox<IOrderEvent> pending = [new OrderSubmitted()];
var extended = pending.AddRange(new IOrderEvent[] { new OrderCancelled() });
var queued = extended.Envelopes[0];
var acknowledged = extended.Remove(queued);
```

`[]`, `[message]`, and `[.. pending, message]` construct **fresh history** with a
fresh revision and consecutive IDs starting at one. Spreading copies payloads,
not IDs, epoch, or the prior sequence high-water mark. Use `Add` / `AddRange` to
extend existing history, and `Clear` to drain it while preserving that history.
`AddRange(messages)` uses one system UTC timestamp for the batch; its overload
`AddRange(messages, utcNow)` accepts an explicit batch timestamp. An empty batch
returns the same snapshot. Batch inputs are enumerated once and buffered together.

`Remove(envelope)` and `Remove(id)` remove only a matching FIFO head.
`RemoveRange(envelopes)` and `RemoveRange(ids)` remove matching occurrences
anywhere, preserving remaining order and sequence history. Use the batch overload
for processor acknowledgements, which may contain gaps. For an empty removal
batch, supply a typed collection: bare `RemoveRange([])` is ambiguous between the
two overloads.

`Add(message)` uses system UTC. When a grain uses an injected clock, sample it
at the call site and pass the instant with
`Add(message, timeProvider.GetUtcNow())`. The persisted outbox never retains
the provider, so serialization and state rehydration need no clock
re-registration.

Use `OutboxProcessor<T>` with the **base payload type**, even for polymorphic
outboxes. Register it in the constructor after the state manager:

```csharp
private readonly IStateManager<OrderState> state;
private readonly OutboxProcessor<IOrderEvent> outboxProcessor;

public OrderGrain([PersistentState("state", "Default")] IPersistentState<OrderState> storage)
{
    state = this.RegisterStateManager("state", storage);
    outboxProcessor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<IOrderEvent>
    {
        OutboxAccessor = () => state.State.Outbox,
        AcknowledgePostedAsync = async (items, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            await state.WriteAsync(state.State with
            {
                Outbox = state.State.Outbox.RemoveRange(items)
            });
        }
    })
    .AddPostman<OrderSubmitted>(async message => await PublishSubmittedAsync(message))
    .AddPostman<OrderCancelled>(async message => await PublishCancelledAsync(message));
}
```

`AddPostman` callbacks take `(message)`, `(message, token)`, or
`(message, token, cancellationToken)`. Both `Task` and `ValueTask` handlers work
without adapters. On C# 13 or newer, overload priority selects `ValueTask` for
ordinary async lambdas; Task-returning method groups and expressions select the
Task overload. Older compilers need an explicitly typed delegate or lambda return
type when a lambda could fit both. See [overload priority](https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/csharp-13.0/overload-resolution-priority). The second argument is always the delivery `OutboxSequenceToken`,
and cancellation is always third. Capture a grain factory when needed, or use
`AddGrainPostman` to resolve the destination. The processor builds that token from the stored
`OutboxMessageId` and its owning grain ID, preserving sequence, epoch and append
timestamp across retries and reactivation. No sender identity is stored in the outbox.

`OutboxAccessor` returns the current `Outbox<T>` snapshot.
`AcknowledgePostedAsync` and `ReconcileFailedAsync` receive its original
stored `OutboxMessageEnvelope<T>` values. Acknowledgment receives exactly the
successfully delivered items, which need not be a contiguous prefix. Remove them
by passing the envelopes directly to `RemoveRange`; never remove by position or count. Equal payloads
can represent different messages and retain distinct stored IDs.

`IOutboxGrain` forwards reminder ticks to the single attached processor.
Register exactly one processor per grain activation; a second registration
throws. Add multiple postmen to that processor when item subtypes need
different delivery behavior. The grain remains responsible for its own
message contracts, posting target, and dead-letter policy.
Postman matching is first-match-wins: register specific message types before
base interfaces or catch-all handlers.

Failed dispatches are reported through `ReconcileFailedAsync`. That callback
is where the owning grain applies retry, dead-letter, max-depth, or trimming
policy, because the grain owns the durable outbox state. The attempt counts
passed to the callback are in-memory per activation (and pruned once an item
is no longer pending), so policies that must survive activation restarts need
to persist their own counters on the items or grain state.

The outbox tools do not require the state manager. When persisting the outbox
with plain `IPersistentState<T>` writes, the pipeline stays at-least-once on
its own: items only leave durable state when the grain removes them in
`AcknowledgePostedAsync` after a successful post, so a failed or ambiguous
state write leaves them pending and at worst causes duplicate delivery, never
loss. `Outbox<T>.Revision` is a persisted UUIDv7 that acts as an outbox-specific ETag.
Each mutation creates a new revision; operations that change nothing preserve it.
`Equals` compares only the revision in O(1), without scanning payloads.
`GetHashCode` also uses only the revision. Competing snapshots remain distinct even
when their append timestamps match. Serialization preserves the revision so
recovery can confirm a successful save whose response was lost. Revisions are
compared for equality, not order, and do not change message IDs or delivery tokens.

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
        async (grain, message) => await grain.ApplyAsync(message));
```

Token-aware stream projections and grain calls also operate on payloads:

```csharp
outboxProcessor
    .AddStreamPostman<OrderSubmitted, SubmittedDelivery>(
        "order-streams",
        message => StreamId.Create("submitted-orders", message.OrderId),
        (message, token) => new SubmittedDelivery(message, token))
    .AddGrainPostman<OrderCancelled, IOrderProjectionGrain>(
        (message, grains) => grains.GetGrain<IOrderProjectionGrain>(message.OrderId),
        async (grain, message, token) => await grain.ApplyAsync(message, token));
```

The projection creates an application-owned transport contract, not a stored outbox
envelope. Stream selection also has a token-aware overload. Cancellable grain
invocations can receive `(grain, message, token, cancellationToken)`. Grain
invocation callbacks support both `Task` and `ValueTask`, with the same priority; stream selectors and projections remain
synchronous, with optional token arguments.

Group registrations that use the same configured provider:

```csharp
processor.ForStreamProvider("events", provider => provider
    .AddStreamPostman<OrderSubmitted>(
        message => StreamId.Create("submitted-orders", message.OrderId))
    .AddStreamPostman<OrderCancelled>(
        message => StreamId.Create("cancelled-orders", message.OrderId)));
```

The group supports the same projections and token-aware selectors as direct
`AddStreamPostman` calls. Each call registers immediately on the original
processor, so registration order remains first-match-wins across both forms.
`ForStreamProvider` selects an existing Orleans provider; it does not install one.
The callback overload returns the original processor, so additional postmen can
be chained after the group. Configuration is synchronous; registrations already
made remain if the callback throws. The builder-returning overload is also available.

Routing and projection choose their token arguments independently. Both direct
and grouped registration support token-aware routing with no projection:

```csharp
processor.AddStreamPostman<OrderSubmitted>("events",
    (message, token) => StreamId.Create("orders-by-sender", token.Sender.ToString()));
```

Or use a projection that only needs the payload:

```csharp
processor.ForStreamProvider("events")
    .AddStreamPostman<OrderCancelled, CancelledDelivery>(
        (message, token) => StreamId.Create("cancelled-by-sender", token.Sender.ToString()),
        message => new CancelledDelivery(message.OrderId));
```

### Migrating existing outbox callers

- Indexing and enumeration now return payloads. Use `outbox.Envelopes` where code
  previously read `.Id` or `.Message` from outbox entries.
- Rename `PendingItems` to `OutboxAccessor`, which returns a non-null `Outbox<T>` directly. Replace array
  conversions with `() => state.Outbox`; return `[]` for a fresh empty snapshot,
  not `default` or `null` (null is rejected with `InvalidOperationException`).
- Acknowledgement and failure callbacks still receive envelopes. Existing ID-based
  removal remains supported. The persisted JSON and Orleans field layout is unchanged.

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

Register `StreamManager` in the grain constructor or `OnActivateAsync` and configure
its subscriptions. Supply a tracker accessor for persisted resume tokens, or omit
it when the grain does not track stream positions. The accessor runs when attaching
or resuming subscriptions, after hydration, and returns the current tracker after
state replacement. Attach explicit subscriptions from `OnActivateAsync`:

```csharp
streamManager = this.RegisterStreamManager(() => state.State.Tracker)
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

The string namespace overload derives a stream id from the complete receiving
`GrainId`, including its grain type and compound-key extension. Publishers must
use the same helper with the target grain identity:

```csharp
var customer = grainFactory.GetGrain<ICustomerGrain>(customerId);
var streamId = StreamManager.CreateStreamId("prices", customer.GetGrainId());
var stream = streamProvider.GetStream<PriceChanged>(streamId);
```

This convention follows the grain type, so renaming that type changes the
derived stream id. Use the `StreamId` overload for an application-owned id that
must survive grain-type changes, or when a custom grain identity cannot
round-trip through Orleans' textual `GrainId` representation:

```csharp
streamManager = this.RegisterStreamManager(() => state.State.Tracker)
    .ConfigureExplicitSubscription<PriceChanged>(
        "StreamProvider",
        StreamId.Create("prices", customerId),
        HandlePriceChangedAsync);
```

The previous key-only convention is not compatible with these full-identity
stream ids. Recreate existing durable subscriptions and update publishers
together, or preserve the previous id through the explicit `StreamId` overload.

Tracked resume tokens are a per-subscription choice. The default is to pass
the previous token when the tracker accessor returns a tracked cursor. Opt out when a
subscription should attach without a resume token:

```csharp
streamManager = this.RegisterStreamManager(() => state.State.Tracker)
    .ConfigureExplicitSubscription<PriceChanged>(
        "StreamProvider",
        "prices",
        HandlePriceChangedAsync,
        useTrackedResumeToken: false);
```

Orleans 10.3 lets `[StatelessWorker]` grains consume streams, but such
consumers use provider-managed live delivery and reject any non-null resume
token. When a stateless worker registers a stream manager with a tracker
snapshot, set `useTrackedResumeToken: false` on its subscriptions, or omit the
snapshot, or Orleans throws `InvalidOperationException` during attach.

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

## JSON Grain Storage

`Outbox<T>`, `OutboxMessageEnvelope<T>`, `OutboxMessageId`, `OutboxSequenceToken`,
`MessageTracker`, and `StreamCursor` carry `[JsonConverter]` attributes, so
they round-trip through any System.Text.Json-based grain storage — including
the Orleans 10.3 `siloBuilder.UseSystemTextJsonGrainStorageSerializer()` —
without extra `JsonSerializerOptions` configuration. Orleans' own
System.Text.Json `StreamSequenceToken` converter only handles
`EventSequenceToken`/`EventSequenceTokenV2`; tokens stored inside
`MessageTracker` or `StreamCursor` bypass it and use the
`StreamSequenceTokenJsonConverters` registry instead, so provider tokens such
as `EnrichedEventHubSequenceToken` persist correctly.

Orleans' default Newtonsoft.Json storage serializer is not supported by these
converters. All library state types are `[GenerateSerializer]`, so they pass
the Orleans 10.3 JSON `$type` allow-list, but the payload shape is not
guaranteed; use a System.Text.Json serializer or the Orleans binary serializer.

## Scope

This package is messaging infrastructure, not an event-sourcing or CQRS framework. It wraps Orleans state, outbox dispatch, receiver deduplication, and stream subscription management while leaving domain modeling, read models, transport targets, and operational policy to the application.

## Beta API changes

The constructor-registration and payload-postman changes tracked in
[issue #179](https://github.com/egil/framework/issues/179) are breaking changes:

- Replace `Outbox<T>.Create(grainId)` with `Outbox<T>.Create()`.
- Outboxes persist a UUIDv7 `Revision`. JSON requires a non-empty revision; previous beta snapshots need migration or reset. Independently constructed snapshots no longer compare equal based on matching contents.
- Stored envelopes expose `Id` (`OutboxMessageId`); delivery tokens are supplied to handlers by the processor.
- Use `OutboxProcessor<TPayload>` and `OutboxProcessorOptions<TPayload>`, not envelope generic arguments.
- Register payload subtypes with `AddPostman`, `AddStreamPostman`, and `AddGrainPostman`. Direct `AddPostman` callbacks take one, two, or three arguments. Direct and grain callbacks accept both `Task` and `ValueTask`, preferring `ValueTask` for async lambdas on C# 13+. Replace `AddPostmanWithToken` with `AddPostman`; move cancellation to the third argument and capture a grain factory rather than receiving it as a callback argument.
- Supply state factories for types without a public parameterless constructor. Custom `IStateManagerFactory` implementations receive the initial-state factory and runtime configuration callback.
- Pass a tracker accessor to `RegisterStreamManager`, for example `() => state.State.Tracker`. It is evaluated when attaching/resuming subscriptions, after hydration, and observes later state replacement.

The earlier sender-free message-ID and revision changes described above changed
the stored JSON shape; migration of snapshots predating those changes is not
provided. The payload-first collection and OutboxAccessor changes preserve that
existing sender-free, revision-bearing JSON and Orleans layout.
