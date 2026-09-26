# Egil.Orleans.Messaging

Composable messaging infrastructure for Microsoft Orleans grains.

`Egil.Orleans.Messaging` provides building blocks for grains that need durable state changes and durable message handoff to move together:

- `IStateManager<T>` wraps `IPersistentState<T>` so a grain does not keep observing uncommitted state after ambiguous write failures.
- `Outbox<T>` stores messages alongside grain state and assigns durable message IDs; processors add sender identity at delivery.
- `OutboxProcessor<T>` dispatches pending outbox items through registered postmen, with retry, reminder forwarding, failure acknowledgement, and telemetry.
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
siloBuilder.AddDefaultStateManager();
```

Pass a name instead when different storage providers need different failure
handling — the factory is what classifies their failures — and then name it at the
registration call too:

```csharp
siloBuilder.AddDefaultStateManager("Default");
siloBuilder.AddAzureStorageStateManager("blobs");

state = this.RegisterStateManager("blobs", storage, () => new OrderState());
```

The default manager re-reads after every failed write or clear. It recognizes
`InconsistentStateException` through exception wrappers and rethrows the original
exception after refreshing state and the ETag, even if the recovered value
matches. Aggregates containing only concurrency conflicts behave the same way;
an aggregate containing an uncertain failure still uses read-back to determine
whether the operation persisted.

For Orleans Azure Table or Blob grain storage, install and configure the
Orleans storage provider separately. The Messaging companion works through
`IPersistentState<T>` and Azure SDK exceptions; it does not select or install
the underlying provider. Install `Egil.Orleans.Messaging.State.AzureStorage`
and register the Azure-aware factory instead:

```csharp
siloBuilder.AddAzureStorageStateManager("state");
```

The Azure-aware manager uses Azure SDK `RequestFailedException.Status` and
`ErrorCode` values to decide recovery. ETag and record-existence conflicts
(including HTTP 412, 409 and 404 unless a specific rejection code applies)
re-read storage to refresh the local state and ETag, then always rethrow.
Authentication/authorization failures, payload/validation failures, missing
containers/tables and non-ETag precondition failures skip recovery reads and
rethrow. Ambiguous or transient outcomes, including HTTP 503 `ServerBusy`,
HTTP 500 `OperationTimedOut`, HTTP 429 throttling, no-response failures and
timeout exceptions, still use read-back recovery to determine whether the
operation persisted.

Register the manager in the grain constructor and keep it in a readonly field. The
facet already carries its names, so the manager does not ask for them again:

```csharp
siloBuilder.AddDefaultStateManager();   // one factory for every managed facet

public sealed class OrderGrain : Grain, IOrderGrain
{
    private readonly IStateManager<OrderState> state;

    public OrderGrain([PersistentState("state", "Default")] IPersistentState<OrderState> storage)
    {
        state = this.RegisterStateManager(storage, () => new OrderState());
    }

    public Task RenameAsync(string name, CancellationToken cancellationToken) =>
        state.WriteAsync(state.State with { Name = name }, cancellationToken);
}
```

`ReadAsync`, `WriteAsync`, and `ClearAsync` accept an optional `CancellationToken`,
which is forwarded to storage and any recovery read. Cancellation is cooperative:
the provider decides whether it can interrupt in-flight work. Existing calls may omit the token; custom
`IStateManager<T>` implementations must update their method signatures. An already canceled
token prevents storage access and write-version stamping.

Cancellation after a write or clear starts does not establish whether it persisted.
If recovery is also canceled, `State` reverts to the last stored value — discarding
an unsaved value, which is not the same as the previously visible snapshot — and the
manager rethrows the original operation exception. Re-read with a fresh token before
another mutation to refresh the state and ETag. A provider-confirmed success is
adopted even if cancellation was requested concurrently.

The overload without a factory needs the state type to be able to represent an absent
record on its own: either `IStateDefault<TSelf>`, or a non-abstract type with a public
parameterless constructor — see [Injecting the manager](#injecting-the-manager). A
state type with neither is rejected at registration, naming the state type and the
grain.
Constructor registration
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
or the default representing absent storage. Interleaved readers cannot observe an
in-flight *write candidate* — a value whose durability is unknown because a write
is still running. An *unsaved* value is different: its durability is knowingly
deferred, so `State` does expose it and `HasUnsavedChanges` reports it (see
[Deferred writes](#deferred-writes)). Do not replace raw
`storage.State` after registration.

Use the optional runtime configuration callback to restore transient dependencies
on each adopted instance, including after reads and recovery:

```csharp
state = this.RegisterStateManager("state", storage,
    () => new OrderState(),
    loaded => loaded.Tracker.RegisterTimeProvider(timeProvider));
```

A state type can carry that need itself instead, by implementing
`IConfigurableState` — see [Injecting the manager](#injecting-the-manager). Prefer
it when every grain holding the state needs the same wiring, which is the usual
case: no call site can then forget it. When both are present the state type's own
configuration runs first and the callback layers on top.

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

The facet must be one backed by an `IGrainStorage` provider. A journaled facet from
`Orleans.Journaling` is also an `IPersistentState<T>`, and that package registers one
for every DI key, so it can reach `RegisterStateManager` by accident — but its
`ReadStateAsync` is a no-op, because a journal is replayed at activation rather than
re-read. Recovery would then compare an attempted write with itself and report a
failure as success, so wrapping one throws `NotSupportedException` at construction.
Use the journal's own durability instead.

State types must be reference types and implement `IEquatable<T>`. For
non-trivial state graphs, inherit from `VersionedState` so the recovery path
compares a library-stamped version rather than relying on structural
collection equality.

`VersionedState.Version` has a public `init` accessor so a consumer's
`JsonSerializerContext` can restore it without a custom resolver. A write stamps
a fresh version on a copy of the record; the input record keeps its original
version. Use `manager.State` after the write to observe the persisted version.

### State manager lifecycle hooks

Use `ConfigureHooks` to rebuild in-memory data or copy confirmed state
elsewhere. Configure an injected or constructor-registered manager in the grain
constructor to observe its initial load:

```csharp
public OrderGrain(
    [PersistentState("state", "Default")] IStateManager<OrderState> state)
{
    state.ConfigureHooks(hooks =>
    {
        hooks.OnRead = loaded => RebuildIndex(loaded);
        hooks.OnChangeAsync = (adopted, operation, recordExists, token) =>
            CopyStateAsync(adopted, operation, recordExists, token);
    });
}
```

For registration inside `OnActivateAsync`, await the registration itself:

```csharp
state = await this.RegisterStateManagerAsync(
    "Default", storage,
    hooks => { hooks.OnRead = loaded => RebuildIndex(loaded); },
    cancellationToken: cancellationToken);
```

The async overloads support default or keyed factories and an optional explicit
default-state factory, like synchronous registration. Initial notification uses
the hydrated state without another storage read. Direct injection and constructor
registration await initial handlers before `OnActivateAsync`; async registration
awaits them before returning. An initial handler failure fails activation.

There are four slots: `OnRead`, `OnWrite`, `OnClear`, and the common `OnChange`.
Each has an `Async` alternative returning `Task` and receiving a cancellation
token. Setting both forms of the same slot is rejected. `ConfigureHooks` replaces
all slots atomically; omitted slots are cleared, and `ConfigureHooks(_ => { })`
removes all hooks. The library supplies a fresh configuration object to the callback;
consumers cannot construct or derive `StateManagerHooks<T>`. The callback runs once,
then the manager validates and copies its handlers. Retaining and editing that object
afterward cannot change installed hooks. A throwing callback or invalid configuration
leaves the previous hooks intact. Configuration never replays state. An operation keeps the configuration it
captured at its start, including if a handler reconfigures the manager.

| Outcome | Notification |
| --- | --- |
| Initial load or successful explicit read, including absent or unchanged state | `Read` |
| Successful write or recovery confirming a lost write response | `Write` |
| Successful clear or recovery confirming a lost clear response | `Clear`, with a fresh default |
| Failed mutation followed by a successful recovery adopting storage state | `Read`, then the original storage error |
| Direct `State` assignment, no-op save, classified non-persistence, or failed recovery | None |

An optimistic concurrency conflict is still reported even when recovery happens
to match the attempted change; its recovery notification is `Read`. Recovery
confirming a mutation emits only its `Write` or `Clear` notification.

State is published first, then transient configuration (`IConfigurableState` and
`configureState`) runs, then the common handler, then the specific handler.
`OnChange` receives the adopted state, `StateManagerOperation`, and `recordExists`;
specific handlers receive only the adopted state (plus a token for async forms).
Transient configuration failure suppresses lifecycle handlers. A common-handler
failure does not suppress the specific handler. Multiple failures produce an
`AggregateException`, ordered storage error first when applicable, then common,
then specific. A single failure is propagated unchanged.

Handlers are awaited and must treat their supplied state as **read-only**. They
may read `manager.State`, but nested reads, writes, clears, and saves on that
manager are rejected. Hooks do not add serialization for overlapping storage
operations on a reentrant grain.

**A hook can fail after storage has durably changed.** Its failure never rolls
back state, retries storage, or starts recovery. Handlers still run if the token
became canceled after confirmed persistence; async handlers receive that same
token and may cancel. Do not interpret a thrown operation as proof that storage
was unchanged. Hooks provide no deduplication across attempts or activations:
external effects must be idempotent, for example keyed by a persisted state
version.

Initial notification belongs to registration; consumers have no separate
initialization step. Configure injected or constructor-registered managers in the
constructor, or await `RegisterStateManagerAsync` inside `OnActivateAsync` when
initial hooks are needed. Configuring hooks after the initial load affects only
subsequent operations and never replays that load. Managers constructed directly
outside registration run hooks on subsequent explicit storage operations.

### Injecting the manager

A grain can inject `IStateManager<T>` directly on its `[PersistentState]` parameter,
in place of `IPersistentState<T>`:

```csharp
public sealed class OrderGrain(
    [PersistentState("state", "Default")] IStateManager<OrderState> state)
    : Grain, IOrderGrain
{
    public Task RenameAsync(string name, CancellationToken cancellationToken) =>
        state.WriteAsync(state.State with { Name = name }, cancellationToken);
}
```

The facet is built underneath exactly as it is for `IPersistentState<T>`, so the
state still hydrates before `OnActivateAsync` and still takes part in the migration
handoff. The attribute keeps naming both the state record and the storage provider,
and the provider name now also selects the keyed `IStateManagerFactory` — so it can
no longer drift from a name repeated at a registration call.

No extra registration is needed: every `AddDefaultStateManager`,
`AddStateManagerFactory`, and `AddAzureStorageStateManager` overload enables this.
Call `services.AddStateManagerFacet()` directly only when registering an
`IStateManagerFactory` by hand. Grains that inject `IPersistentState<T>` are
unaffected.

Because there is no call site, a state factory and a configuration callback are
expressed on the state type instead. Both are optional:

```csharp
[GenerateSerializer]
public sealed record OrderState : IStateDefault<OrderState>, IConfigurableState
{
    [NonSerialized] private TimeProvider? clock;

    [Id(0)] public Guid Id { get; init; }
    [Id(1)] public MessageTracker Tracker { get; init; } = new();

    // Replaces the createInitialState factory. Not written to storage.
    public static OrderState CreateDefault(IGrainContext context) =>
        new() { Id = context.GrainId.GetGuidKey() };

    // Replaces the configureState callback. Runs on every adopted instance.
    public void Configure(IGrainContext context)
    {
        clock = context.ActivationServices.GetRequiredService<TimeProvider>();
        Tracker.RegisterTimeProvider(clock);
    }
}
```

`IGrainContext.ActivationServices` is the same DI scope the grain's own constructor
is resolved from, so anything the grain could inject — keyed services included — is
reachable from `Configure` and `CreateDefault`, alongside the grain key and grain
type.

Both contracts apply however the manager was obtained, so a grain that keeps using
`RegisterStateManager` gets them too. A `createInitialState` factory passed there
overrides `CreateDefault`, and a `configureState` callback runs after `Configure`.
A state type implementing neither resolves an absent record to `new TState()`, which
needs a non-abstract type with a public parameterless constructor. An abstract type
is rejected even when it declares one, because nothing can call it. Anything else
fails at activation with a message naming the state type and the grain.

### Deferred writes

`State` has a setter. Assigning it moves the visible snapshot forward **without**
writing to storage, exactly as assigning `IPersistentState<T>.State` does, so
several changes can be folded into one write. `HasUnsavedChanges` reports that the
visible value is not durable yet, and `SaveChangesAsync(cancellationToken)` persists
it — or does nothing when there is nothing outstanding:

```csharp
state.State = state.State with { Outbox = state.State.Outbox.RemoveRange(delivered) };

// ... later, on the next business change, one write carries both:
await state.WriteAsync(state.State with { Name = name }, cancellationToken);
```

The two ways to persist differ in **when the value becomes visible**:

| | visible | durable |
|---|---|---|
| `State = x` then `SaveChangesAsync()` | immediately | at the save |
| `WriteAsync(x)` | only if the write succeeded | on success |

Use `WriteAsync(value)` when a reply must not be derived from a value that never
persisted; assign `State` when the grain should see the change now and pay for the
write later.

Assignment does not stamp a new `VersionedState.Version`; an unsaved snapshot is not
a storage revision, so stamping happens when the value is actually written. The
runtime configuration callback does run on the assigned instance, as it does for
every other adopted instance.

**An unsaved value is lost if the activation ends before it is written.** Persist on
the way out:

```csharp
public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
{
    try
    {
        await state.SaveChangesAsync(cancellationToken);
    }
    catch (Exception ex)
    {
        // Deactivation is not retried, and its token can already be cancelled on a
        // forced shutdown, so this write can fail. Losing the unsaved value costs a
        // redelivery; letting the failure escape costs the rest of deactivation.
        logger.LogWarning(ex, "Could not save state while deactivating.");
    }

    await base.OnDeactivateAsync(reason, cancellationToken);
}
```

The library does not do this for you. A lifecycle observer never receives the
`DeactivationReason`, so it could not tell an idle deactivation from a silo
shutdown or a failure, and the stop token is routinely already cancelled by then.
Calling `SaveChangesAsync` unconditionally is safe: with nothing outstanding it does
not reach storage and does not observe the cancellation token. With unsaved changes
it does observe the token, which is why the write is guarded — an already-cancelled
deactivation would otherwise throw out of the hook and skip the rest of it.

Keep it unconditional. Filtering on `DeactivationReason` is tempting, but every
reason code you skip is a reason code that drops unsaved data, and `ShuttingDown` is
an orderly, expected event on every deployment.

Failures discard unsaved work rather than preserving it. A failed write reverts
`State` to the last value storage confirmed and clears `HasUnsavedChanges`; a
successful `ReadAsync` or `ClearAsync` discards it too, because storage wins. The
marker is true only while `State` holds a value that no storage operation has
confirmed. An operation that settles nothing changes nothing and leaves the unsaved
value visible and flagged, so the call can simply be retried — that covers an
already-cancelled token, a rejected argument, and a `ReadAsync` whose storage call
throws, which learns nothing about a value it never wrote. A read that *does*
return settles the question, so the marker clears before the record is resolved:
an invalid record or a throwing default-state factory is reported to you and does
not resurrect the unsaved value.

**Live migration is one of those deactivations.** This library takes no part in
Orleans' migration handoff — a migrating activation persists like any other, and the
destination reads what storage holds. Orleans runs `OnDeactivateAsync` before it
dehydrates, so the write above lands first and the destination inherits a durable
value.

Skip that write and the unsaved value still rides along inside the storage facet
Orleans carries itself, but the destination cannot tell it was never written: it
treats the value as durable and loses it at its own next deactivation. A stage made
before the grain's first write is worse — the facet arrives with no record, so the
destination resolves the configured default and the change is gone on arrival.

## Journaling companion (preview)

The optional [Egil.Orleans.Messaging.Journaling package](src/Egil.Orleans.Messaging.Journaling/README.md)
provides `IDurableMessageTracker` and `IDurableOutbox<T>`. They share an Orleans
journal commit with business state while storing incremental messaging changes.
`AsImmutable()` returns the current immutable value for existing processor integration.

The companion is built and released with Egil.Orleans.Messaging by the same
workflow. Its NuGet version uses the matching messaging version with a `-preview`
suffix, and it targets Orleans Journaling 10.3.1-alpha.1. Installing core OM does
not install Journaling.
See the package README for registration, grain composition, storage requirements,
format compatibility, and runnable verification.

## Outbox

Store an `Outbox<T>` on the grain state and commit messages with the business state change:

```csharp
[GenerateSerializer]
public sealed record OrderState : VersionedState
{
    [Id(0)] public string? Name { get; init; }

    [Id(1)] public Outbox<IOrderEvent> Outbox { get; init; } = [];
}

public async Task SubmitAsync(CancellationToken cancellationToken)
{
    var next = state.State with
    {
        Outbox = state.State.Outbox.Add(new OrderSubmitted())
    };

    await state.WriteAsync(next, cancellationToken);
    await outboxProcessor.PostInBackgroundAsync(cancellationToken);
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
            await state.WriteAsync(state.State with
            {
                Outbox = state.State.Outbox.RemoveRange(items)
            }, ct);
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
`AcknowledgePosted`, `AcknowledgePostedAsync`, and `AcknowledgeFailuresAsync`
receive its original stored `OutboxMessageEnvelope<T>` values. The configured
posted acknowledgement callbacks receive exactly the successfully delivered items,
which need not be a contiguous prefix. Remove them by passing the envelopes directly
to `RemoveRange`; never remove by position or count. Equal payloads can represent
different messages and retain distinct stored IDs.

To avoid paying a storage write per acknowledgement, stage the removal instead and
let the next business write carry it:

```csharp
AcknowledgePosted = items =>
{
    state.State = state.State with
    {
        Outbox = state.State.Outbox.RemoveRange(items)
    };
}
```

`OutboxAccessor` reads through the state manager, so it observes the deferred
removal with no change at the call site. This is safe because items only leave the
*durable* outbox once an acknowledgement is persisted: losing a deferred
acknowledgement causes redelivery, never message loss. Pair it with the
deactivation hook from [Deferred writes](#deferred-writes).
Without it, a grain that stops doing business writes never drains its durable
outbox. Each activation that posts redelivers the same items, stages the
acknowledgement, and loses it again at deactivation; within that activation later
post runs see the deferred, empty view and do nothing. Nor does it recover on a
timer — the deferred removal empties the view the processor reconciles against, so
retry and the reminder are disabled, and registering a processor does not post on
activation. The items sit in storage until a fresh activation reads them back and
something posts again.

Two consequences of the processor seeing the deferred view are worth planning for.
The processor reconciles its retry timer and reminder against `OutboxAccessor`, so
a deferred acknowledgement that empties the outbox **disables retry** — correctly,
as far as the processor can tell, though on the strength of a removal that is not
durable yet. Anything that later discards the change brings those items back as
pending without re-arming the processor: a `WriteAsync` that fails, a successful
`ReadAsync` or `ClearAsync`, which let storage win, or — in a `[Reentrant]` grain,
or with `InterleaveAcknowledgementCallbacks` on — a business write that was already
in flight when the assignment happened and finishes by adopting its own value. Call
`PostInBackgroundAsync` in any of those cases; a grain that does nothing else can
leave the batch waiting until something posts again. Second, a redelivery is a real delivery: receivers must
already be idempotent for at-least-once, and deferring makes the duplicate path
slightly more likely, not differently shaped.

`IOutboxGrain` forwards reminder ticks to the single attached processor.
Register exactly one processor per grain activation; a second registration
throws. Add multiple postmen to that processor when item subtypes need
different delivery behavior. The grain remains responsible for its own
message contracts, posting target, and dead-letter policy.
Postman matching is first-match-wins: register specific message types before
base interfaces or catch-all handlers.

Failed dispatches are reported through `AcknowledgeFailuresAsync`. That callback
is where the owning grain applies retry, dead-letter, max-depth, or trimming
policy, because the grain owns the durable outbox state. The attempt counts
passed to the callback are in-memory per activation (and pruned once an item
is no longer pending), so policies that must survive activation restarts need
to persist their own counters on the items or grain state.

The outbox tools do not require the state manager. When persisting the outbox
with plain `IPersistentState<T>` writes, the pipeline stays at-least-once on
its own: items only leave durable state when the grain removes them in
a posted acknowledgement callback after a successful post, so a failed or
ambiguous state write leaves them pending and at worst causes duplicate delivery, never
loss. `Outbox<T>.Revision` is a persisted UUIDv7 that acts as an outbox-specific ETag.
Each mutation creates a new revision; operations that change nothing preserve it.
`Equals` compares only the revision in O(1), without scanning payloads.
`GetHashCode` also uses only the revision. Competing snapshots remain distinct even
when their append timestamps match. Serialization preserves the revision so
recovery can confirm a successful save whose response was lost. Revisions are
compared for equality, not order, and do not change message IDs or delivery tokens.

If a post run fails before acknowledgement completes — for example when the
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
`AcknowledgePosted`, `AcknowledgePostedAsync`, or `AcknowledgeFailuresAsync`.
Postmen run on Orleans' activation scheduler, not on the .NET thread pool.
Acknowledgement callbacks are non-interleaving by default: they do not
interleave with normal grain calls unless
`InterleaveAcknowledgementCallbacks` is enabled. Reentrant grains can still
interleave according to Orleans' normal scheduling rules.
Pending items in a post run are dispatched concurrently. Successful items are
still acknowledged as one ordered batch after all dispatches complete, and
failed items are acknowledged as one batch.

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

When the projection enriches the payload instead of transforming it — stamping it
with something from its delivery token and returning the same type — name the
payload type once:

```csharp
processor.AddStreamPostman<OrderSubmitted>(
    "events",
    message => StreamId.Create("submitted-orders", message.OrderId),
    (message, token) => message with { Source = token.Sender.ToString() });
```

This is the recommended alternative to storing the sender in the outbox payload.
Both direct and grouped registration offer it, with either stream selector shape.

### Outbox metrics

The `egil.orleans.messaging` meter reports `outbox.grains.pending` as an
observable up/down counter, tagged by `grain.type`. It counts active activations
whose latest processor-observed outbox is nonempty, not the number of messages.
Empty/nonempty transitions adjust the total once; deactivation removes an
activation's contribution automatically. Previously observed types report zero
when none remain pending.

Counts stay current without listeners, so attaching or reconnecting collection
reports the existing total. Shared telemetry stores only a total per grain-type
label, never a registry of grains or their messages. Collection reads those totals
without invoking grain code.

This is a process-local snapshot, including all silos hosted in that process.
Across deployed processes, sum distinct `service.instance.id` series. Inactive
grains with persisted backlog are invisible until reactivated and observed by the
processor. Outbox mutations become visible at the processor's next snapshot read;
this includes background scheduling, dispatch, acknowledgement reconciliation,
and failure retry scheduling. Deferred acknowledgement writes mean this is not
a measure of durable storage backlog.

Alert on sustained pending counts alongside `outbox.post.errors` and
`outbox.post.items`, and monitor exporter health separately. A nonempty outbox
alone does not prove that delivery is stuck.

### OpenTelemetry trace correlation

Adding a message to the outbox captures the current `Activity` as a W3C
traceparent and stores it with the message. Capture happens when the message is
added, not when it is delivered: the processor drains on a grain timer, on a
reminder, or on whichever request happens to trigger the drain, and by then the
activity that caused the message has usually ended. A drain also flushes every
pending message at once, so reading the ambient activity at delivery time would
attribute messages to whichever request triggered the flush.

There is nothing to configure. `Add`, `AddRange`, and collection-expression
construction all capture `Activity.Current` implicitly:

```csharp
// Inside a request with an active Activity.
state = state with { Outbox = state.Outbox.Add(new OrderSubmitted(orderId)) };
await stateManager.WriteAsync(state);
```

At delivery the processor starts one `orleans.outbox.post` producer span per
message and links it to the captured context. Postmen run inside that span, so
Orleans grain calls and stream adapters that propagate `Activity.Current` — such
as `EnrichedEventHubAdapter` — carry the right trace to the receiver with no
extra work.

Delivery spans **link** back to the producing request rather than being parented
under it. A message can be delivered hours after the request that produced it
ended, and parenting into a finished trace produces orphaned spans and traces
that stretch across the whole delay. When a request does drive the drain, the
delivery span joins that request's trace and still links to the producing one.

The traceparent is stored whether or not the producing activity was sampled, so
the trace id remains available for log correlation. `tracestate` is not
captured.

On the receiving side of a stream, `StreamManager` links each
`orleans.stream.process` span to the producer by default, and a subscription
can opt in to joining the producer's trace instead. See
[Stream trace correlation](#stream-trace-correlation).

#### Rebuilding an outbox from stored data

Ambient capture is the right default for the producing path and wrong for every
path that *reconstructs* history — a state migration, an import, a replay. Those
run under whatever activity happens to be current; for a migration inside
`JsonMigratable` deserialization that is the grain activation, or whichever
inbound call triggered it. It has nothing to do with the request that originally
produced the message, possibly days earlier. Stamping it makes the delivery span
link to an unrelated trace, which is worse than linking to nothing: a wrong link
is indistinguishable from a right one when reading a trace.

Use `Outbox<T>.Restore`, which never reads `Activity.Current`:

```csharp
// In IMigrateFrom<TV1, TV2>: reconstructing history, not producing messages.
var messages = Outbox<IEvseOutboxEvent>.Restore(
    source.Outbox.Select(item => (
        item,
        item.Timestamp != default ? item.Timestamp : migratedAt)));
```

Sequence assignment stays inside the outbox: payloads get consecutive numbers
from `1` in enumeration order, and the first timestamp becomes the epoch.
Timestamps are normalized to UTC and need not be ordered — receivers deduplicate
on epoch and sequence number, not on time.

When the old format kept no per-message timestamp, pass one for the batch:

```csharp
var messages = Outbox<IEvseOutboxEvent>.Restore(source.Outbox, migratedAt);
```

When you are moving messages between outboxes and the original IDs must survive,
restore the envelopes themselves. This overload preserves sequence numbers,
timestamps, epoch, and each message's traceparent verbatim:

```csharp
var messages = Outbox<IEvseOutboxEvent>.Restore(
    previousOutbox.Envelopes,
    previousOutbox.LatestSequenceNumber);
```

The high-water mark is a required argument rather than something inferred from
the envelopes, because `Envelopes` holds only what is still *pending*. A source
that had already delivered and removed its highest-numbered messages would
otherwise restore to a lower mark, the next `Add` would hand out a sequence
number the receiver has already seen, and the receiver would reject that message
as a duplicate. Passing a mark below the last envelope's sequence number throws
`ArgumentOutOfRangeException`, and so does passing a nonzero mark with no
envelopes at all: a mark only means something inside an epoch — receivers compare
epochs first and sequence numbers only within the same epoch — and an empty
restore has no envelope to take an epoch from. Restore a fully drained source
with `Outbox<T>.Create()` instead, which starts a fresh sequence space.

Envelope sequence numbers must also be positive, because `Add` assigns from `1`
and a restore should not be able to build state the producing path cannot reach.

Because it is also the one entry point that accepts caller-supplied identity, it
validates that too: sequence numbers must strictly increase in enumeration order
and every envelope must carry the same epoch, or it throws `ArgumentException`.
`Remove` only matches a FIFO head and receivers deduplicate against a per-epoch
high-water mark, so a mis-ordered restore would produce an outbox whose messages
the receiver silently drops.

Collection expressions are never the right tool for a rebuild. Building one with
`Outbox<T> x = [...]` captures `Activity.Current` and has no suppressing form —
the `[CollectionBuilder]` contract fixes its signature — and it resets the epoch
and sequence space as well.

### Migrating existing outbox callers

- Indexing and enumeration now return payloads. Use `outbox.Envelopes` where code
  previously read `.Id` or `.Message` from outbox entries.
- Rename `PendingItems` to `OutboxAccessor`, which returns a non-null `Outbox<T>` directly. Replace array
  conversions with `() => state.Outbox`; return `[]` for a fresh empty snapshot,
  not `default` or `null` (null is rejected with `InvalidOperationException`).
- Acknowledgement and failure callbacks still receive envelopes. Existing ID-based
  removal remains supported. The persisted JSON and Orleans field layout is unchanged.
- Rebuilding an outbox from a previously persisted shape — an `IMigrateFrom`
  implementation, an import, a replay — must use `Outbox<T>.Restore` rather than a
  loop of `Add` calls, so historical messages are not stamped with the trace context
  of the activity doing the rebuilding. See *Rebuilding an outbox from stored data*.

## Receiver Dedup

`MessageTracker` accepts a message only when its stream token, stream cursor, or outbox token advances the stored high-water mark:

```csharp
if (!state.State.Tracker.TryAcceptMessage("prices", token, out var tracker))
{
    return;
}

await state.WriteAsync(state.State with { Tracker = tracker });
```

When a stream delivery includes a provider sequence token, `StreamManager` supplies it from the runtime together with the provider name, so nothing has to ride on the payload. Tokenless stream deliveries are accepted without advancing `MessageTracker` state.
The outbox token reaches a receiver as an argument instead, from `AddPostman` and
`AddGrainPostman`, and a receiver that passes it to `TryAcceptMessage` rejects a
message the producer sent again after an acknowledgement was lost.
`AddStreamPostman` publishes the payload alone, so an outbox message delivered over
a stream is deduplicated by its stream token like any other stream message. That
split is deliberate: delivery identity stays off the payload, so no event contract
has to grow a field to carry it.

Use `LatestStreamSequenceToken("prices")` when all you need is the previous
resume token. Keep using `LatestStream("prices")` when you need the full
cursor or must distinguish "no stream tracked" from "tracked stream with a
null token".

The tracker can also evict old sender or stream entries when your retention policy allows it.

`OutboxSequenceToken.TryGetTraceParent(out var traceParent)` exposes the
traceparent captured when the sender added the message, so a receiver can link
its own span back to the request that produced the message:

```csharp
if (token.TryGetTraceParent(out var traceParent)
    && ActivityContext.TryParse(traceParent, traceState: null, isRemote: true, out var producer))
{
    using var activity = MySource.StartActivity(
        "order.submitted.process",
        ActivityKind.Consumer,
        parentContext: default,
        links: [new ActivityLink(producer)]);
}
```

Use a link rather than a parent, for the same reason the processor does. The
traceparent is not part of delivery identity: dedup ignores it, and two tokens
that differ only by traceparent address the same message.

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
            if (!state.State.Tracker.TryAcceptMessage(cursor, out var tracker))
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

### Subscription options

Each subscription takes an optional `configure` callback that receives a
`StreamSubscriptionOptions`:

| Option                  | Default                     | Effect                                                              |
|-------------------------|-----------------------------|---------------------------------------------------------------------|
| `UseTrackedResumeToken` | `true`                      | Pass the tracker's last cursor token when attaching or resuming.    |
| `OnError`               | `null` (log the error)      | Called with the namespace and exception when the handler throws.    |
| `Trace`                 | `MessageTraceOptions.Link`   | How the consumer span relates to the producer's trace.              |
| `TimeProvider`          | registered, else `System`   | Clock for `MessageTraceOptions.ParentWithinLag`.                     |

```csharp
streamManager = this.RegisterStreamManager(() => state.State.Tracker)
    .ConfigureExplicitSubscription<PriceChanged>(
        "StreamProvider",
        "prices",
        HandlePriceChangedAsync,
        options =>
        {
            options.UseTrackedResumeToken = false;
            options.OnError = LogStreamError;
        });
```

Set silo-wide defaults once instead of repeating them in every grain. Every
subscription starts from these defaults, and its own callback overrides them:

```csharp
siloBuilder.ConfigureStreamManager(options =>
{
    options.Trace = MessageTraceOptions.ParentWithinLag(TimeSpan.FromMinutes(5));
    options.OnError = (streamNamespace, error) => Log.StreamHandlerFailed(streamNamespace, error);
});
```

The overload that also receives the silo's `IServiceProvider` shares a
registered service, such as a keyed domain clock, with every subscription:

```csharp
siloBuilder.ConfigureStreamManager((options, services) =>
    options.TimeProvider = services.GetRequiredKeyedService<TimeProvider>("pricing"));
```

Both overloads exist on `IServiceCollection` too. Calls add up in
registration order, as `services.Configure<StreamSubscriptionOptions>(...)`
does.

Orleans 10.3 lets `[StatelessWorker]` grains consume streams, but such
consumers use provider-managed live delivery and reject any non-null resume
token. When a stateless worker registers a stream manager with a tracker
snapshot, set `UseTrackedResumeToken = false` on its subscriptions, or omit the
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

When the Event Hub carries a payload format the library cannot decode, subclass
the adapter and override `CreateInnerBatchContainer` to supply your own batch
container. The adapter still attaches the enriched token, so the container only
has to decode:

```csharp
public sealed class DataPlatformAdapter(string providerName, Serializer serializer, ILogger logger)
    : EnrichedEventHubAdapter(providerName, serializer)
{
    protected override IBatchContainer CreateInnerBatchContainer(EventHubMessage message)
        => new DataPlatformBatchContainer(message, logger);
}
```

Register the subclass with Orleans' `UseDataAdapter`. The container must be
`[GenerateSerializer]`, since it is delivered to consumers inside the adapter's
wrapper, and it does not need to produce sequence tokens: the adapter replaces
the batch token and every per-event token.

The core package can consume provider-specific token metadata through
`IStreamSequenceTokenMetadata` without taking a direct Event Hubs dependency.
Custom stream providers that expose custom `StreamSequenceToken` types should
register a `JsonConverter<TToken>` with `StreamSequenceTokenJsonConverters`
during startup.

### Stream trace correlation

`StreamManager` wraps every delivery in an `orleans.stream.process` consumer
span. When the token carries a valid W3C traceparent, such as the one
`EnrichedEventHubAdapter` stamps on publish, each subscription chooses how that
span relates to the producer through `StreamSubscriptionOptions.Trace`:

| `Trace`                                  | Consumer span                                                        |
|------------------------------------------|----------------------------------------------------------------------|
| `MessageTraceOptions.Link`               | New trace, with an `ActivityLink` to the producer span. The default. |
| `MessageTraceOptions.Parent`             | Child of the producer span, in the producer's trace. No link.        |
| `MessageTraceOptions.ParentWithinLag(t)` | Child when `now - enqueued <= t`, otherwise linked as with `Link`.   |
| `MessageTraceOptions.None`               | No span. The handler runs under the ambient activity.                |

`None` turns off the span only. The `stream.*` metrics are still recorded.

```csharp
streamManager = this.RegisterStreamManager(() => state.State.Tracker)
    // External feed: one trace per delivery.
    .ConfigureImplicitSubscription<PriceChanged>("prices", HandlePriceChangedAsync)
    // Internal grain-to-grain fan-out: keep the causal flow in one trace.
    .ConfigureImplicitSubscription<SessionUpdated>(
        "session-updates",
        HandleSessionUpdatedAsync,
        options => options.Trace = MessageTraceOptions.ParentWithinLag(TimeSpan.FromMinutes(5)));
```

When most streams in the silo are internal, make parenting the silo default with
`ConfigureStreamManager` and set `Link` on the external subscriptions instead.

Keep `Link` for external or high-volume streams, and for any stream whose
consumers can publish back into a loop. Every delivery then gets its own trace,
and a producer's trace never stretches across a backlog.

Choose `Parent` or `ParentWithinLag` for internal streams with bounded fan-out,
where one request should read as one trace. Backends that build the transaction
tree from the trace id, such as the Application Insights end-to-end view,
ignore links. Tail samplers decide per trace id, so linked consumer traces are
sampled independently of the producer and are usually dropped.

`ParentWithinLag` guards against the backlog case. After an outage, consumers
catch up on messages enqueued hours earlier. With `Parent`, those spans join
the old producer traces and stretch them across the whole outage. With
`ParentWithinLag`, deliveries older than the limit fall back to a link.
`ParentWithinLag` needs a token that exposes an enqueue time, such as
`EnrichedEventHubSequenceToken`. Without one it always links. The lag is
measured with `StreamSubscriptionOptions.TimeProvider`. When that is `null`,
the default, the `TimeProvider` registered in the silo's services is used, or
`TimeProvider.System` when none is registered.

A linked span always starts its own trace, even when the delivery runs under an
ambient activity. A missing or unparseable traceparent gives a span with no
link, whatever the mode. It joins the ambient activity when there is one, and
starts a new trace otherwise.

### Registering the converters outside a silo

Any process that deserializes grain state containing Event Hub tokens needs
these converters, including processes that never configure an Event Hub stream
provider — a test fixture on in-memory storage using the production
`JsonSerializerOptions`, a tool that reads grain state blobs offline, a
background archiver. Register them directly, with or without a container:

```csharp
EventHubStreamSequenceTokenJsonConverters.Register();
```

```csharp
services.AddEventHubStreamSequenceTokenJsonConverters();
```

Registration is idempotent, so these and `UseEnrichedDataAdapter()` can be
combined in any order — a silo that does both is fine, and no registrar has to
run first. `StreamSequenceTokenJsonConverters.Register(...)` throws only on a
genuine conflict, where a *different* converter claims a type descriptor that is
already taken. Do not wrap registration in `try`/`catch
(InvalidOperationException)`: there is no duplicate to swallow, and it would
hide exactly the conflict worth knowing about.

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

- `StreamManager` subscription settings moved into a `configure` callback.
  `ConfigureImplicitSubscription` and `ConfigureExplicitSubscription` no
  longer take `onError` or `useTrackedResumeToken`; set
  `StreamSubscriptionOptions.OnError` and
  `StreamSubscriptionOptions.UseTrackedResumeToken` instead. Settings shared
  by every grain can move to `siloBuilder.ConfigureStreamManager(...)`.

  ```diff
  - .ConfigureImplicitSubscription("prices", HandleAsync, LogStreamError, useTrackedResumeToken: false)
  + .ConfigureImplicitSubscription("prices", HandleAsync, options =>
  + {
  +     options.OnError = LogStreamError;
  +     options.UseTrackedResumeToken = false;
  + })
  ```

- `StorageFailureKind` gained a `Conflict` value and provider classifiers now
  route optimistic-concurrency rejections through it. Previously
  `AzureStorageStateManager<T>` classified `InconsistentStateException`,
  HTTP 412, and ETag/existence error codes (`ConditionNotMet`,
  `UpdateConditionNotSatisfied`, `BlobAlreadyExists`/`BlobNotFound`,
  `EntityAlreadyExists`/`EntityNotFound`, `ResourceAlreadyExists`/`ResourceNotFound`)
  as `DidNotPersist`, which skipped read-back and left the facet holding the
  stale ETag until the grain deactivated or called `ReadAsync()`. They are now
  classified as `Conflict`, which forces a recovery read to refresh the local
  baseline before rethrowing the original exception, so the next `WriteAsync`
  uses a fresh ETag ([issue #261](https://github.com/egil/framework/issues/261)).
  Custom `StateManagerBase<T>` overrides that returned `DidNotPersist` for
  ETag-mismatch failures should return `Conflict` instead; other failures
  (auth, missing container/table, payload too large) still return
  `DidNotPersist`.

- `IStateManager<T>` gains `ConfigureHooks(Action<StateManagerHooks<T>>)`. Custom
  provider implementations should derive from `StateManagerBase<T>` to inherit
  atomic hook replacement. Wrappers should forward configuration to their underlying
  manager. Hook configuration objects are library-owned, so independent implementations
  cannot construct the callback argument themselves. Registration handles awaited
  initial notification internally; there is no public initialization method.
  Existing `IStateManagerFactory` signatures are unchanged. Configure hooks in the constructor for injected or
  constructor-registered managers; use and await `RegisterStateManagerAsync` when
  registering with hooks in `OnActivateAsync`. Keep transient dependency wiring
  in `configureState`; move confirmed-storage effects to lifecycle hooks.
  Both configuration and async registration accept a callback such as
  `hooks => { hooks.OnRead = loaded => RebuildIndex(loaded); }`; consumers do not
  instantiate hook configuration objects.

- `VersionedState.Version` now uses public `init` so state records can be included
  in a consumer's System.Text.Json source-generated context
  ([issue #224](https://github.com/egil/framework/issues/224)). Remove any
  reflection-only serialization workaround and add the state type to your
  `JsonSerializerContext`. `IStateManager<T>.WriteAsync` now stamps a copy, so
  code that reads the version after a write should use `manager.State.Version`
  rather than the input record's version. Stored JSON remains compatible.

- Added the opt-in `Egil.Orleans.Messaging.Journaling` preview package with named durable trackers and outboxes; the core immutable APIs remain available.

`outbox.depth` is removed because it reported the last observed activation's
message count rather than aggregate backlog. Migrate backlog dashboards and alerts
to `outbox.grains.pending`, which counts active activations with observed pending
work and explicitly reports zero after they drain or deactivate
([issue #221](https://github.com/egil/framework/issues/221)). Adjust thresholds to
count activations rather than messages. Existing success/error metrics are unchanged.

A grain can now inject `IStateManager<T>` on its `[PersistentState]` constructor
parameter instead of `IPersistentState<T>`, and a state type can supply its own
default and runtime configuration through the new `IStateDefault<TSelf>` and
`IConfigurableState` interfaces — see
[Injecting the manager](#injecting-the-manager)
([issue #190](https://github.com/egil/framework/issues/190)). Existing grains need
no change; `RegisterStateManager` keeps working and gains the same state-type
contracts.

Two `RegisterStateManager` overloads are **binary breaking**. The ones that take no
state factory — `RegisterStateManager(storage)` and
`RegisterStateManager(storageName, storage)` — gained an optional `configureState`
parameter, so runtime configuration no longer forces a caller to also supply a
factory. An optional parameter preserves source compatibility but not the emitted
method signature, so assemblies compiled against an earlier version must be rebuilt.

Those two overloads also change behaviour for a state type implementing
`IStateDefault<TSelf>`: an absent record now resolves through `CreateDefault` rather
than `new TState()`. Nothing changes for a state type that does not implement it.

They no longer constrain `TState : new()`, so a state type that implements
`IStateDefault<TSelf>` in place of a public parameterless constructor can use them
without supplying a redundant state factory. Relaxing a constraint is source- and
binary-compatible. The cost is that a state type with neither is now caught at
registration rather than by the compiler.

`OutboxProcessorOptions<T>.AcknowledgePosted` is a synchronous alternative to
`AcknowledgePostedAsync` for acknowledgements that do not perform asynchronous
work. Configure at least one callback; when both are set, `AcknowledgePosted`
runs first. `AcknowledgePostedAsync` is no longer a required member, so a
synchronous-only configuration does not need to return a completed `ValueTask`.

`OutboxProcessorOptions<T>` renames two members so the post-dispatch callbacks
read as one pair:

- `ReconcileFailedAsync` becomes `AcknowledgeFailuresAsync`.
- `InterleaveReconciliationCallbacks` becomes `InterleaveAcknowledgementCallbacks`.

These two renames do not alter delegate signatures, defaults, or behaviour, so
updating the names is the whole migration. "Reconcile" previously named both the
callback pair and the separate step that matches the retry timer and reminder against the
`OutboxAccessor` snapshot; it now means only the latter.

Replace `MessageTracker.ProcessMessage(...)` with `TryAcceptMessage(...)` for all
stream and outbox overloads. It returns the acceptance decision and the next
tracker; it does not execute the message handler or persist the tracker. Tokenless
stream messages are accepted without advancing tracking state.

`IStateManager<T>.State` gains a setter, and the interface gains
`HasUnsavedChanges` and `SaveChangesAsync(CancellationToken)`
([issue #188](https://github.com/egil/framework/issues/188)). This breaks custom
implementations of the interface both at **source** — they must add the setter and
the two members — and at **binary**: an assembly compiled against an earlier version
no longer satisfies the interface and fails to load its implementation until it is
rebuilt. Recompile consumers rather than mixing versions. Managers deriving from
`StateManagerBase<T>` inherit them and need no change.

`WriteAsync(newState)` is unchanged, including its guarantee that the value becomes
visible only if the write succeeded. `State` may now return an unsaved value — see
[Deferred writes](#deferred-writes) for what that narrows
and what it does not.

The constructor-registration and payload-postman changes tracked in
[issue #179](https://github.com/egil/framework/issues/179) are breaking changes:

- Replace `Outbox<T>.Create(grainId)` with `Outbox<T>.Create()`.
- Outboxes persist a UUIDv7 `Revision`. JSON requires a non-empty revision; previous beta snapshots need migration or reset. Independently constructed snapshots no longer compare equal based on matching contents.
- Stored envelopes expose `Id` (`OutboxMessageId`); delivery tokens are supplied to handlers by the processor.
- Use `OutboxProcessor<TPayload>` and `OutboxProcessorOptions<TPayload>`, not envelope generic arguments.
- Register payload subtypes with `AddPostman`, `AddStreamPostman`, and `AddGrainPostman`. Direct `AddPostman` callbacks take one, two, or three arguments. Direct and grain callbacks accept both `Task` and `ValueTask`, preferring `ValueTask` for async lambdas on C# 13+. Replace `AddPostmanWithToken` with `AddPostman`; move cancellation to the third argument and capture a grain factory rather than receiving it as a callback argument.
- Supply state factories for types without a public parameterless constructor. Custom `IStateManagerFactory` implementations receive the initial-state factory and runtime configuration callback.
- Pass a tracker accessor to `RegisterStreamManager`, for example `() => state.State.Tracker`. It is evaluated when attaching/resuming subscriptions, after hydration, and observes later state replacement.

Outbox messages now carry the producer's W3C traceparent:

- `OutboxMessageId` gains a fourth positional parameter, `TraceParent`, which
  defaults to `null`. `OutboxSequenceToken` gains a matching optional
  constructor parameter, a `TraceParent` property, and
  `TryGetTraceParent(out string?)`.
- Both are **binary breaking**. An optional parameter preserves source
  compatibility, not the emitted CLR constructor, so assemblies compiled against
  an earlier version throw `MissingMethodException` until they are rebuilt.
  Recompile consumers rather than mixing versions.
- `OutboxMessageId`'s generated `Deconstruct` is now four-valued, so
  `var (sequenceNumber, timestamp, epoch) = id;` no longer compiles. Add the
  fourth position or discard it with `_`.
- `TraceParent` is excluded from equality and hash code on both types, because
  it is diagnostic metadata rather than identity. An id rebuilt by hand from a
  sequence number, timestamp, and epoch still matches the stored id of a message
  added under an active `Activity`, and `MessageTracker.LatestOutbox` still
  returns a token equal to the one it accepted.
- The property is nullable and omitted from JSON when absent, so snapshots
  written before this change load unchanged. No migration or reset is required,
  unlike the `Revision` change above.
- `Outbox<T>.Restore(...)` is new and **additive**: nothing existing changes and no
  recompile is needed. Reach for it wherever you rebuild an outbox from stored data,
  so the rebuilding activity is not recorded as the producer of historical messages.

The enriching `AddStreamPostman` overloads added for
[issue #187](https://github.com/egil/framework/issues/187) are additive, with one
narrow source break:

- A registration that omits type arguments entirely, passes explicitly typed
  lambdas, and projects to exactly the payload type now reports an ambiguity
  between the one- and two-type-parameter overloads. Add the single type
  argument, as in `AddStreamPostman<OrderSubmitted>(...)`.
- Registrations that already name one or two type arguments are unaffected and
  continue to bind to the same overload.

The earlier sender-free message-ID and revision changes described above changed
the stored JSON shape; migration of snapshots predating those changes is not
provided. The payload-first collection and OutboxAccessor changes preserve that
existing sender-free, revision-bearing JSON and Orleans layout.
