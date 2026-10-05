# Egil.Orleans.Messaging

Composable messaging infrastructure for Microsoft Orleans grains.

`Egil.Orleans.Messaging` provides building blocks for grains that need durable state changes and durable message handoff to move together:

- `IStateManager<T>` wraps `IPersistentState<T>` so a grain does not keep observing uncommitted state after ambiguous write failures.
- `Outbox<T>` stores messages alongside grain state and assigns durable message IDs; processors add sender identity at delivery.
- `OutboxProcessor<T>` dispatches pending outbox items through registered postmen, with retry, reminder forwarding, failure acknowledgement, and telemetry.
- `MessageTracker` tracks stream positions by default, offers opt-in outbox stream receipts, and records sender high-water marks for explicit RPC tokens.
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

## Orleans compatibility

All stable Messaging libraries target `net10.0` and retain Orleans **10.3.1** as
their supported minimum: core, `State.AzureStorage`, and `Streams.EventHubs`.
Production dependencies stay at that minimum. A host can resolve a newer Orleans
version without recompiling these packages; support for 10.4 requires the
[packed-consumer matrix](scripts/verify-orleans-compatibility.ps1) to pass for the
same artifacts on both versions. Dependency metadata alone does not establish
binary or provider compatibility.

`Journaling` is a separate preview integration. Its Orleans preview version and
host requirements can move independently. Do not use its dependency floor as the
minimum for the stable libraries.

To verify freshly built packages, run from `Egil.Orleans.Messaging`:

```powershell
pwsh -File scripts/verify-orleans-compatibility.ps1 -PackageDirectory ./artifacts/packages
```

Add `-IncludeJournaling` after integrating the 10.4 Journaling migration to check
all four packages and run the preview suite on 10.4. The script checks dependency
groups and actual resolved Orleans versions, replaces test project references
with package references, verifies loaded OM DLL hashes, and runs the existing
behavioral suites in isolated output directories and fresh package caches.
It records package hashes, source revision, working-tree state and scope in
`artifacts/compatibility/<version>-<run>/evidence.json` after the stable suites pass.
Preview acceptance is recorded separately in `journaling-evidence.json`, so a
preview failure preserves successful stable evidence.
The Azure and Event Hubs suites exercise adapters and storage failure handling;
they do not establish external broker or Azure storage restart behavior.

When upgrading a host to Orleans 10.4, review these application-owned changes:

- Request latency uses `orleans-app-requests-latency`, a histogram measured in
  fractional milliseconds. Update exporter Views, dashboards and alerts that use
  the removed bucket/count/sum instruments or `duration` labels.
  [Upstream change](https://github.com/dotnet/orleans/pull/11251).
- Grain metrics use the canonical `grain_type` dimension instead of CLR `type`.
  OM's own instruments are separate.
  [Upstream change](https://github.com/dotnet/orleans/pull/11253).
- Existing SQLite hosts need the corrected idempotent Orleans main and
  persistence scripts. OM does not install or migrate these databases.
  [Upstream change](https://github.com/dotnet/orleans/pull/11354).
- Memory-stream `MaxAddCount` is host tuning; its default remains 100.
  [Upstream change](https://github.com/dotnet/orleans/pull/11238).
- Audit application grain RPC interfaces with explicit parameter `[Id]`
  attributes or non-trailing `CancellationToken` parameters before a rolling
  upgrade. Orleans 10.4 honors explicit parameter IDs and excludes cancellation
  tokens from automatic serialized-parameter numbering. Changing those wire IDs
  needs a coordinated contract rollout. These IDs are distinct from `[Id]` on
  serialized state members. OM ships no application grain RPC interfaces; the
  examples above and below keep cancellation tokens last.
  [Upstream change](https://github.com/dotnet/orleans/pull/11179).

Cancellation-aware reminder adoption and subscription start positions remain
separate optional work. Start-position controls require upgrading pulling-agent
silos before enabling them. This compatibility work does not enable either
feature or claim NativeAOT support.

## State Manager

Default and Azure managers use `FenceAndDeactivate`: a failed storage mutation
permanently closes the manager and requests grain deactivation. Opt into `ReadBack`
when retaining the activation is safe and valuable; see [Choosing a recovery policy](#choosing-a-recovery-policy).

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

With `ReadBack` selected, the default manager re-reads after every failed write or clear. It recognizes
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

With `ReadBack` selected, the Azure-aware manager uses Azure SDK `RequestFailedException.Status` and
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
By default, cancellation reported by storage fences the manager. With `ReadBack`,
if recovery is also canceled, `State` reverts to the last stored value — discarding
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

While the manager is usable, `State` exposes the loaded or successfully written snapshot,
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
re-read. `ReadBack` could then compare an attempted write with itself and report a
failure as success. Fencing skips that comparison, but the wrapper still requires
meaningful explicit reads: wrapping a journaled facet throws `NotSupportedException`
under either policy. Use the journal's own durability instead.

State types must be reference types and implement `IEquatable<T>`. This constraint
remains because the manager supports read-back recovery. Ordinary immutable records
are sufficient under fencing, including records containing immutable collections.

`VersionedState` primarily helps **read-back recovery recognize a persisted write**
without depending on structural equality. This matters for `ImmutableArray<T>` and
other immutable collections whose equality is based on their backing references:
a deserialized collection can contain identical values yet compare unequal.
Read-back compares the stamped version instead. Fencing performs no recovery
comparison, so inheriting from `VersionedState` offers less recovery value there.

`VersionedState.Version` has a public `init` accessor so a consumer's
`JsonSerializerContext` can restore it without a custom resolver. A write stamps
a fresh version on a copy of the record; the input record keeps its original
version. Use `manager.State` after the write to observe the persisted version.
Existing versioned types keep version stamping under **both** policies. The version
does **not** provide storage optimistic concurrency; the provider is responsible
for that, typically through ETags.

### Choosing a recovery policy

`FenceAndDeactivate` is the default for Default and Azure state managers. It is the
safer default because a failed storage mutation may leave more than the persisted
facet out of date: private grain fields, derived caches, or partially updated
in-memory bookkeeping may also be inconsistent. Fencing permanently closes the
manager and requests deactivation, allowing subsequent work to use a fresh
activation whose persisted state is loaded again.

`ReadBack` reconciles the storage facet while retaining the activation. It can be
valuable when grain initialization is expensive—for example, rebuilding a large
private cache or establishing subscriptions—and the grain can safely retain its
other fields after reconciliation. The application must ensure those fields are
still valid or repair them. Storage read-back alone cannot do that.

Both policies normally incur a storage read before useful work resumes: `ReadBack`
reads during recovery, while fencing leaves that read to the next activation.
Fencing additionally pays for grain construction and activation lifecycle work;
its cost depends on the grain. Subsequent routing and activation also give Orleans
an opportunity to resolve ownership through the grain directory, which may reveal
an already active instance on another silo. This is not a guarantee that fencing
prevents all duplicate activations or replaces provider concurrency checks.

| Outcome | `FenceAndDeactivate` (default) | `ReadBack` (opt-in) |
| --- | --- | --- |
| Write/clear succeeds | Adopt the result | Adopt the result |
| Ambiguous storage failure | Fence, request deactivation, rethrow | Read storage and reconcile |
| Write committed but response was lost | Report the original failure | May report success if recovery proves persistence |
| Conflict | Fence and rethrow | Refresh state/ETag and rethrow |
| Classified as definitely not persisted | Fence and rethrow | Restore the last stored snapshot and rethrow |
| Recovery read fails | No recovery read in this activation | Restore last stored snapshot, rethrow original; read again before another mutation |
| Failed read, invalid argument, pre-storage cancellation | No permanent fence | Existing state remains available |
| Storage write/clear reports cancellation | Fence | Apply read-back recovery rules |
| State configuration or lifecycle handler throws | Propagate without fencing | Propagate without fencing |

Fencing records the original exception before requesting deactivation. Failure to
request deactivation cannot replace the storage exception or undo the fence.
Recording the activation's failure evidence is best effort and cannot prevent
the separate deactivation request.
`State` and `HasUnsavedChanges` remain readable for local inspection. They retain
the last published snapshot and its marker, including unsaved assignments, without
adopting the failed write candidate or reading storage. They cannot establish the
current durable state. State assignment, hook configuration, storage reads, writes,
clears, and **even no-op saves** throw `StateManagerFencedException` (derived from
`InvalidOperationException`) with the original failure as `InnerException`.
A storage read cannot revive it, and a
deactivation-time `SaveChangesAsync` cannot flush staged data. Handle that failure
in deactivation cleanup just as other save failures are handled. Neither policy
automatically retries the command, queues operations, or flushes pending changes.

The exception's `FailureKind` carries the existing `StorageFailureKind`
classification: `Conflict`, `DidNotPersist`, or `UnknownOutcome`. Both policies
invoke `ClassifyWriteFailure` or `ClassifyClearFailure` once per storage failure.
`ReadBack` uses that result for reconciliation; fencing preserves it without
additional storage reads. If a custom classifier throws while fencing, the kind
is `UnknownOutcome` and the original storage failure is still preserved. The
initial failed operation continues to throw its original exception.

Inspect `state.LastFailureKind` for the latest write or clear failure classification
under either policy. It starts as `null` and resets to `null` when `ReadAsync`,
`WriteAsync`, `ClearAsync`, or `SaveChangesAsync` completes successfully, including
a no-op save and a mutation recovered as success by read-back. A successful recovery
read which still leaves the original mutation throwing retains its classification.
Reads which fail, validation failures, state assignment, property access, and hook
configuration do not clear or replace it. Callback failures are not classified;
an operation which throws from a callback does not count as a successful reset.
A throwing classifier records `UnknownOutcome`; its existing exception behavior
under each policy is unchanged. Once fenced, the manager retains the classification
which caused its fence, including through late completions. This diagnostic is
not a guarantee about the current durable state and does not replace the activation's
fencing record used for outbox recovery.

Deactivation uses `ApplicationError` and retains the original exception. Its
description includes the state type, operation, and classification, for example:
`State manager for 'Example.OrderState' was fenced after WriteAsync failed. Storage failure: Conflict.`

### Recovery configuration

Precedence is **library defaults → silo-wide configuration → factory-registration
configuration → grain-local configuration**. Every manager gets an isolated options
snapshot after hydration which stays fixed for its lifetime.

`state.Options` exposes those effective settings as an immutable
`StateManagerOptionsSnapshot`, currently containing `RecoveryPolicy`. The mutable
`StateManagerOptions` passed to configuration callbacks remains configuration input;
retaining and changing it cannot change a manager's settings. Registered handles
expose `Options` and `LastFailureKind` after hydration, starting in `OnActivateAsync`.
Both getters remain readable after fencing and perform no storage I/O.

```csharp
// Shared baseline for every named and unkeyed manager in this silo.
siloBuilder.ConfigureStateManager(options =>
{
    options.RecoveryPolicy = StateRecoveryPolicy.FenceAndDeactivate;
});
siloBuilder.AddDefaultStateManager();
siloBuilder.AddDefaultStateManager("Orders");

// Retain this activation when reconciliation is safe and rebuilding is expensive.
siloBuilder.AddAzureStorageStateManager("Archive", options =>
{
    options.RecoveryPolicy = StateRecoveryPolicy.ReadBack;
});

// Override only this manager, retaining state initialization and runtime wiring.
state = this.RegisterStateManager("Orders", storage,
    createInitialState: static () => new OrderState(),
    configureState: loaded => loaded.AttachRuntimeServices(services),
    configure: options => options.RecoveryPolicy = StateRecoveryPolicy.ReadBack);
```

`ConfigureStateManager` and the factory helpers are available on both `ISiloBuilder`
and `IServiceCollection`. Global configuration also has an overload accepting
`Action<StateManagerOptions, IServiceProvider>` for silo-local dependencies.
Global callbacks use `ConfigureAll` semantics; factory callbacks use named
post-configuration, so factory overrides win even if globals are registered later.
Callbacks within a layer run in registration order. Storage names identify named
options; unkeyed factories use `Options.DefaultName`. Options are freshly created,
then the grain callback runs, and the result is copied and validated before the
factory receives it.

Direct `[PersistentState] IStateManager<T>` injection inherits global and factory
settings. Use explicit synchronous or asynchronous registration for a grain-local
override. The async callback comes before `cancellationToken`.

Direct construction accepts `recoveryPolicy` and optional `grainContext` after
`configureState`; it does not implicitly resolve silo configuration. Without a
context the manager still fences, but cannot request deactivation. Its owner must
reload durable storage **before constructing a replacement**; simply wrapping the
same facet again could adopt the failed write candidate. Journaling preview APIs
and their recovery behavior are unchanged.

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

The recovery notifications below apply only to `ReadBack`; fencing skips adoption
and lifecycle handlers for the failed mutation.

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
Calling `SaveChangesAsync` inside the guarded cleanup is safe: a fenced manager
rejects the save even when nothing is outstanding, and the catch lets cleanup continue.
Otherwise, with nothing outstanding it reaches neither storage nor the cancellation token. With unsaved changes
it does observe the token, which is why the write is guarded — an already-cancelled
deactivation would otherwise throw out of the hook and skip the rest of it.

Keep it unconditional. Filtering on `DeactivationReason` is tempting, but every
reason code you skip is a reason code that drops unsaved data, and `ShuttingDown` is
an orderly, expected event on every deployment.

With fencing, failures preserve the readable local snapshot and unsaved-changes
marker, but permanently reject changes and deactivation-time saves.
With `ReadBack`, failures discard unsaved work rather than preserving it. A failed write reverts
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
suffix, and it targets Orleans Journaling 10.4.0-alpha.1. All OM packages now
require Orleans 10.4.0; upgrade the host and Orleans packages together. Installing core OM does
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
    outboxProcessor = this.RegisterOutboxProcessor(() => state.State.Outbox, options =>
    {
        options.AcknowledgePostedAsync = async (items, ct) =>
        {
            await state.WriteAsync(state.State with
            {
                Outbox = state.State.Outbox.RemoveRange(items)
            }, ct);
        };
    })
    .AddPostman<OrderSubmitted>(async message => await PublishSubmittedAsync(message))
    .AddPostman<OrderCancelled>(async message => await PublishCancelledAsync(message));
}
```

Constructor registration automatically installs the deactivation safeguard. If
registering the processor in `OnActivateAsync` or later, enable it once on the
silo builder before starting the host:

```csharp
siloBuilder.ConfigureOutboxProcessor();
```

Both existing `ConfigureOutboxProcessor` overloads that accept options also
enable the safeguard. Orleans requires lifecycle subscriptions before activation
starts; late processor registration without this setup throws with instructions.

`AddPostman<TSub>` takes one handler. Only the payload is required; the delivery
`OutboxSequenceToken`, `IGrainFactory`, and `CancellationToken` are independently
optional, in that relative order. Every shape supports both `Task` and `ValueTask`:

| Handler parameters | ValueTask priority | Task priority |
| --- | ---: | ---: |
| `(message)` | 1 | 0 |
| `(message, token)` | 5 | 4 |
| `(message, grains)` | 3 | 2 |
| `(message, cancellationToken)` | 1 | 0 |
| `(message, token, grains)` | 3 | 2 |
| `(message, token, cancellationToken)` | 5 | 4 |
| `(message, grains, cancellationToken)` | 1 | 0 |
| `(message, token, grains, cancellationToken)` | 1 | 0 |

On C# 13 or newer, [overload priority](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-13.0/overload-resolution-priority.md)
chooses the highest-priority applicable overload. This preserves the token-based
defaults when parameters are unused: `(message, _)` receives the delivery token,
and `(message, _, _)` receives the delivery token and cancellation token. Within
each shape, ordinary async lambdas prefer `ValueTask`; Task-returning method
groups and expressions use the Task overload. The parameter types and lambda
body determine applicability, not parameter names. An explicitly typed handler
can select a particular shape; older compilers may need explicit parameter and
return types when a lambda fits multiple overloads.

Capture a grain factory if it is already in scope, or request it as a handler
argument. Resolve the destination and call it inside that same handler.
The processor builds a requested delivery token from the stored
`OutboxMessageId` and its owning grain ID, preserving sequence, epoch and append
timestamp across retries and reactivation. No sender identity is stored in the outbox.

The first argument, the outbox accessor, returns the current `Outbox<T>` snapshot.
`AcknowledgePosted`, `AcknowledgePostedAsync`, and `AcknowledgeFailuresAsync`
receive its original stored `OutboxMessageEnvelope<T>` values. The configured
posted acknowledgement callbacks receive exactly the successfully delivered items,
which need not be a contiguous prefix. Remove them by passing the envelopes directly
to `RemoveRange`; never remove by position or count. Equal payloads can represent
different messages and retain distinct stored IDs.

To avoid paying a storage write per acknowledgement, stage the removal instead and
let the next business write carry it:

```csharp
options.AcknowledgePosted = items =>
{
    state.State = state.State with
    {
        Outbox = state.State.Outbox.RemoveRange(items)
    };
};
```

The outbox accessor reads through the state manager, so it observes the deferred
removal with no change at the call site. This is safe because items only leave the
*durable* outbox once an acknowledgement is persisted: losing a deferred
acknowledgement causes redelivery, never message loss. Pair it with the
deactivation hook from [Deferred writes](#deferred-writes).
Without it, a grain that stops doing business writes never drains its durable
outbox. Each activation that posts redelivers the same items, stages the
acknowledgement, and loses it again at deactivation; within that activation later
post runs see the deferred, empty view and do nothing. Nor does it recover on a
timer — the deferred removal empties the view the processor reconciles against, so
local retry stops. `OnDeactivation` removes its reminder when this view becomes empty;
`KeepRegistered` retains a slow fallback until deactivation, but also removes it
if that view is still empty at shutdown. Neither policy reads storage to check
whether an acknowledgement was persisted. Persist deferred acknowledgements in
the grain's deactivation hook before the processor's cleanup; otherwise durable
items can remain without a wakeup until a fresh activation explicitly posts them.

Two consequences of the processor seeing the deferred view are worth planning for.
The processor reconciles its retry timer and reminder against the outbox accessor, so
a deferred acknowledgement that empties the outbox **disables local retry** — correctly,
as far as the processor can tell, though on the strength of a removal that is not
durable yet. Anything that later discards the change brings those items back as
pending without re-arming the processor: a `WriteAsync` that fails, a successful
`ReadAsync` or `ClearAsync`, which let storage win, or — in a `[Reentrant]` grain,
or with `InterleaveAcknowledgementCallbacks` on — a business write that was already
in flight when the assignment happened and finishes by adopting its own value. Call
`PostInBackgroundAsync` in any of those cases; a grain that does nothing else can
leave the batch waiting until something posts again (or a retained reminder ticks).
Second, a redelivery is a real delivery: receivers must
already be idempotent for at-least-once, and deferring makes the duplicate path
slightly more likely, not differently shaped.

### Processor options and silo defaults

The `configure` callback receives an `OutboxProcessorOptions<T>`. It must set
`AcknowledgePosted` or `AcknowledgePostedAsync`, and can override the shared
scheduling settings:

| Option                               | Default                    | Effect                                                          |
|--------------------------------------|----------------------------|-----------------------------------------------------------------|
| `ProcessingTimeout`                  | 20 seconds                 | Maximum time per post run.                                      |
| `RetryDelay`                         | 2 minutes                  | Grain-timer delay for normal retries; independent of reminder recovery periods. |
| `ReminderPolicy`                     | `OnDeactivation`           | Register only during pending deactivation, or use `KeepRegistered` for an activation fallback. |
| `ActiveReminderPeriod`               | 5 minutes                  | Recovery reminder period for pending work, including after deactivation; minimum one minute. |
| `IdleReminderPeriod`                 | 1 hour                     | KeepRegistered fallback period; configure longer than idle collection age plus a collection/deactivation margin. |
| `Interleave`                         | `true`                     | Let other grain calls run while postmen await.                  |
| `InterleaveAcknowledgementCallbacks` | `false`                    | Let the acknowledgement callbacks interleave.                   |
| `KeepAlive`                          | `false`                    | Keep the activation alive while items are pending.              |
| `TimeProvider`                       | registered, else `System`  | Clock for `ProcessingTimeout` and `ParentWithinLag`.            |
| `Trace`                              | `MessageTraceOptions.Link` | How the `orleans.outbox.post` span relates to the producer.     |

Set the shared settings once per silo instead of repeating them in every grain.
Each processor starts from these defaults, and its own callback overrides them:

```csharp
siloBuilder.ConfigureOutboxProcessor((options, services) =>
{
    options.RetryDelay = TimeSpan.FromMinutes(1);
    options.KeepAlive = true;
    options.TimeProvider = services.GetRequiredKeyedService<TimeProvider>("pricing");
});
```

The silo defaults take the non-generic `OutboxProcessorOptions`, so they cannot set
the acknowledgement callbacks, which are typed to each grain's payload. Both
overloads exist on `IServiceCollection` too, and calls add up in registration
order.

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
processor attempts to arm its retry timer before rethrowing. The caller receives
the original exception but does not need to schedule retries.
With the default `OutboxReminderPolicy.OnDeactivation`, both successful posts and
active retries create no reminders. Grain timers provide the normal retry cadence
using `RetryDelay`. A durable reminder is registered only during orderly
deactivation with pending work. When an inherited reminder wakes the grain, timer
processing resumes; draining stops the local timers and removes the known reminder.
An abrupt silo crash can bypass deactivation, so this policy cannot establish a
reminder for that case.

To keep a fallback available across successful batches, configure:

```csharp
options.ReminderPolicy = OutboxReminderPolicy.KeepRegistered;
options.IdleReminderPeriod = TimeSpan.FromHours(1);
options.ActiveReminderPeriod = TimeSpan.FromMinutes(5);
options.RetryDelay = TimeSpan.FromMinutes(2);
```

`KeepRegistered` starts registration during activation. Constructor-attached
processors register through the lifecycle hook. A processor attached inside
`OnActivateAsync` or a grain method starts registration immediately; posts await
the shared operation before dispatching or scheduling. If registration must finish
before the first business write, await an empty post first. A failed asynchronous
activation registration logs `OutboxActivationReminderFailed`; a subsequent post
or orderly shutdown retries it.

The idle fallback uses `IdleReminderPeriod` (default one hour). **Set it longer
than the grain's idle collection age**, with a margin for collection scans and
deactivation. Orleans reminder ticks reset idleness, so a shorter period could
keep an otherwise idle grain alive. The interval is configured independently of
Orleans collection settings; it does not automatically track per-grain overrides.
Ordinary successful posts reuse the fallback without reminder I/O. When a retry
is needed, one update switches to `ActiveReminderPeriod` (default five minutes).
Both reminder periods are independent of `RetryDelay` and must be at least one
minute. Timers drive retries; reminders are conservative recovery wakeups. A
successful drain restores the idle period once.
Empty deactivation removes the fallback, ideally before it ever fires. Traffic or
delayed collection can still keep an activation alive long enough for a fallback tick.

Neither policy checks whether a reminder exists before registering. Until a
matching tick arrives, assume no inherited reminder exists. Registration directly
upserts and can reset an inherited schedule. When cleanup is needed, use the local
handle; only an inherited tick without a local handle justifies a lookup to obtain
one. A delayed tick can belong to an already removed reminder, so it does not
suppress a later required retry registration. Overlapping registrations, updates
and removals are serialized and reuse completed state.

With either policy, orderly deactivation waits for in-flight reminder work, then
applies the following policy:

| Outbox observation | Deactivation action |
| --- | --- |
| Unfenced, readable and empty | Normal idle cleanup; no new reminder |
| Unfenced, readable with pending entries | Ensure a reminder at `ActiveReminderPeriod` |
| All recorded fencing failures are `Conflict` | Neither register nor remove a reminder |
| Any fencing failure is `UnknownOutcome` or `DidNotPersist` | Ensure a reminder at `ActiveReminderPeriod`, even if the local snapshot is empty |

State managers record fencing on the grain context before requesting deactivation,
so the processor can recognize it even while local state remains readable. If
several managers fail, a conflict cannot hide another manager's uncertain outcome.
An accessor which throws `StateManagerFencedException` is also supported.
If local inspection throws an unrelated exception after fencing was recorded,
the processor still follows the recorded recovery policy, with an unknown count.
Skipping confirmed conflicts deliberately leaves recovery to
the competing owner or a subsequent activation. A conflict does **not** prove
that another activation is still alive. Other fenced outcomes cannot establish
whether durable outbox work remains, so a recovery reminder is needed even if its
next activation ends up doing nothing. An empty local snapshot can precede a failed
write which actually persisted new messages; readable does not mean current.

No inherited tick and no local handle means no empty cleanup call.
These operations respect the deactivation cancellation budget and
are best effort; an abrupt silo crash or unavailable reminder store can still
prevent recovery.

A failed deactivation registration emits `OutboxDeactivationReminderFailed` with
`GrainId`, `GrainType`, `ReminderName`, nullable `OutboxItemCount`, and the exception,
identifying work that may need manual reactivation and an explicit post. The count
is the observed number of entries when readable, or `null` when unknown. Successful
fallback registration after fencing emits no failure warning. Cleanup failures instead emit
`OutboxReminderRemovalFailed`; delivery and deactivation continue, and a later
drain, tick or deactivation can retry cleanup.

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

Then resolve the postman by name from the grain activation service provider.
`ConfigureOutbox` stands for the grain's options callback, as in the example
above:

```csharp
outboxProcessor = this.RegisterOutboxProcessor(() => state.State.Outbox, ConfigureOutbox)
    .AddPostman<OrderSubmitted>("orders");
```

For common Orleans targets, use the built-in helpers instead of writing the
callback by hand:

```csharp
outboxProcessor = this.RegisterOutboxProcessor(() => state.State.Outbox, ConfigureOutbox)
    .AddStreamPostman<OrderSubmitted>(
        "order-streams",
        message => StreamId.Create("submitted-orders", message.OrderId));
```

```csharp
outboxProcessor = this.RegisterOutboxProcessor(() => state.State.Outbox, ConfigureOutbox)
    .AddPostman<OrderSubmitted>((message, grains) =>
        grains.GetGrain<IOrderProjectionGrain>(message.OrderId).ApplyAsync(message));
```

Token-aware stream projections and grain calls also operate on payloads:

```csharp
outboxProcessor
    .AddStreamPostman<OrderSubmitted, SubmittedDelivery>(
        "order-streams",
        message => StreamId.Create("submitted-orders", message.OrderId),
        (message, token) => new SubmittedDelivery(message, token))
    .AddPostman<OrderCancelled>((message, token, grains) =>
        grains.GetGrain<IOrderProjectionGrain>(message.OrderId).ApplyAsync(message, token));
```

The projection creates an application-owned transport contract, not a stored outbox
envelope. Stream selection also has a token-aware overload. Cancellable grain
invocations can receive all four handler arguments:

```csharp
outboxProcessor.AddPostman<OrderSubmitted>((message, token, grains, ct) =>
    grains.GetGrain<IOrderProjectionGrain>(message.OrderId).ApplyAsync(message, token, ct));
```

When `GrainFactory` is already in scope, the handler can capture it:

```csharp
outboxProcessor.AddPostman<OrderSubmitted>(message =>
    GrainFactory.GetGrain<IOrderProjectionGrain>(message.OrderId).ApplyAsync(message));
```

Stream selectors and projections remain synchronous, with optional token arguments.

Publish the same payload to multiple streams with one registration. Supply a fixed
list of destinations:

```csharp
processor.AddStreamPostman<OrderSubmitted>("events",
[
    StreamId.Create("orders", "audit"),
    StreamId.Create("orders", "analytics")
]);
```

Or select destinations from each message:

```csharp
processor.AddStreamPostman<OrderSubmitted>("events", message =>
[
    StreamId.Create("orders", message.OrderId),
    StreamId.Create("orders", "audit")
]);
```

The selector can also receive the `OutboxSequenceToken`:

```csharp
processor.AddStreamPostman<OrderSubmitted>("events", (message, token) =>
[
    StreamId.Create("orders", message.OrderId),
    StreamId.Create("orders-by-sender", token.Sender.ToString())
]);
```

These overloads publish the original payload to each supplied stream ID using the
same provider and outbox token. Repeated IDs cause repeated publications. Fixed
collections are retained without copying, so supply a repeatable enumerable; changes
to its contents affect subsequent deliveries. Selectors run once per delivery attempt.
Destination sequences are enumerated as messages are published. An empty fixed list
or selector result is a successful no-op: nothing is published and the item is acknowledged.
Null collections are invalid. Fixed registrations reject null at registration;
a selector returning null fails delivery and leaves the item pending.

Publications are awaited sequentially in destination order, and the item is
acknowledged only after all succeed. A publication or enumeration failure stops the
attempt; earlier publications may already have succeeded. Retries select destinations
again and publish from the beginning. Keep selectors stable across
retries when every originally selected destination must receive the message.
Streams that already succeeded may receive it again with the same outbox token;
receivers can use `MessageTracker.TryAcceptMessage(cursor, out tracker)` to suppress
duplicate business effects per stream. There is no transaction across streams or
separate acknowledgement per destination.

Group registrations that use the same configured provider:

```csharp
processor.ForStreamProvider("events", provider => provider
    .AddStreamPostman<OrderSubmitted>(
        message => StreamId.Create("submitted-orders", message.OrderId))
    .AddStreamPostman<OrderCancelled>(
        message => StreamId.Create("cancelled-orders", message.OrderId)));
```

The group supports the same fixed destination lists, selectors, and projections as direct
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

By default, delivery spans **link** back to the producing request rather than
being parented under it. A message can be delivered hours after the request that
produced it ended, and parenting into a finished trace produces orphaned spans and
traces that stretch across the whole delay. When a request does drive the drain,
the delivery span joins that request's trace and still links to the producing one.

`OutboxProcessorOptions.Trace` changes that, per processor or as a silo default.
It takes the same `MessageTraceOptions` as stream subscriptions:

| `Trace`                                  | `orleans.outbox.post` span                                                          |
|------------------------------------------|-------------------------------------------------------------------------------------|
| `MessageTraceOptions.Link`               | Joins the ambient activity, if any, and links to the captured traceparent. Default. |
| `MessageTraceOptions.Parent`             | Child of the captured traceparent instead of the ambient activity. No link.         |
| `MessageTraceOptions.ParentWithinLag(t)` | `Parent` when the message was added at most `t` ago, otherwise `Link`.              |
| `MessageTraceOptions.None`               | As `Link` when a request drives the drain; no span on timer or reminder drains.     |

```csharp
siloBuilder.ConfigureOutboxProcessor(options =>
    options.Trace = MessageTraceOptions.ParentWithinLag(TimeSpan.FromMinutes(5)));
```

`ParentWithinLag` measures the message's age with the processor's
`TimeProvider`, so a retry from a timer or reminder long after the message was
added falls back to a link. Because postmen run inside the span, a stream
published through `EnrichedEventHubAdapter` carries the span's traceparent.

`None` joins an existing trace and never starts one, on both sides. Choose it
to silence background drains and redeliveries while keeping spans inside
request-driven work: a drain driven by a request gets the same span as `Link`,
and a timer or reminder drain, where nothing is ambient, gets none. Its postmen
then run with no activity, so `EnrichedEventHubAdapter` stamps no traceparent.
The `outbox.post.*` metrics are still recorded, and `Outbox<T>` still captures
each message's traceparent for log correlation.

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
- Replace `PendingItems` with the outbox accessor, the first argument of
  `RegisterOutboxProcessor(() => state.Outbox, options => ...)`, which returns a
  non-null `Outbox<T>` directly. Replace array conversions with `() => state.Outbox`;
  return `[]` for a fresh empty snapshot, not `default` or `null` (null is
  rejected with `InvalidOperationException`).
- Acknowledgement and failure callbacks still receive envelopes. Existing ID-based
  removal remains supported. The persisted JSON and Orleans field layout is unchanged.
- Rebuilding an outbox from a previously persisted shape — an `IMigrateFrom`
  implementation, an import, a replay — must use `Outbox<T>.Restore` rather than a
  loop of `Add` calls, so historical messages are not stamped with the trace context
  of the activity doing the rebuilding. See *Rebuilding an outbox from stored data*.

## Receiver Dedup

`AddStreamPostman` publishes plain domain events with stable outbox identity in
Orleans request context. `StreamManager` captures that identity into `StreamCursor`
and removes the reserved context entry while invoking application code. The
receiver chooses whether to use that identity for deduplication:

| `StreamTrackingMode` | Duplicate detection | Retained state |
| --- | --- | --- |
| `StreamPosition` (default) | Rejects provider positions at or below the retained checkpoint. A republished outbox item at a new position remains eligible. | One checkpoint per provider + full stream ID. |
| `OutboxIdentity` | Tagged deliveries use exact outbox receipts, including retries at new provider positions. Untagged deliveries use stream positions. | Checkpoints plus one receipt per accepted logical identity per stream source. |

Position tracking suits idempotent handlers, such as a charge point accepting
updates from many short-lived charging sessions over a stable set of streams.
Sender identity does not increase the stored state in this mode. The source count
can still grow if every session has its own stream ID.

Provider-position high-water marks assume earlier positions have been processed
or can be discarded as stale. They are not a reorder buffer; ordering depends on
the [Orleans stream provider](https://learn.microsoft.com/en-us/dotnet/orleans/streaming/streams-programming-apis#stream-order-and-sequence-tokens).
Neither mode changes the provider's delivery guarantees. Receipt tracking can
give effectively-once committed state changes while a receipt remains retained;
it does not provide exactly-once delivery or atomic external side effects.

### Global settings and per-grain overrides

Automatic eviction is disabled by default. Configure defaults on the silo builder
or `IServiceCollection`:

```csharp
siloBuilder.ConfigureMessageTracker(options =>
{
    options.StreamTrackingMode = StreamTrackingMode.StreamPosition;
    options.RetentionPeriod = null;
});
```

Leave `RetentionPeriod` unset or set it to `null` to retain entries until explicit
eviction. To opt in, set a positive duration such as `TimeSpan.FromDays(14)`. It
applies to stream checkpoints, stream receipts, and explicit RPC sender entries.

A grain can override the defaults on its tracker. Restore overrides in the state
configuration hook so they apply after activation, explicit reads, and recovery:

```csharp
[GenerateSerializer]
public sealed record ReceiptReceiverState : IConfigurableState
{
    [Id(0)] public MessageTracker Tracker { get; init; } = new();

    public void Configure(IGrainContext context) => Tracker.Configure(options =>
    {
        options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity;
        options.RetentionPeriod = TimeSpan.FromDays(7);
    });
}
```

The callback starts with the effective settings; setting only the mode inherits
the duration. Set `RetentionPeriod = null` to disable a global duration for this
grain. Settings are captured when configured, inherited by immutable updates, and
excluded from persisted state and value equality. With `IPersistentState<T>` or
custom storage, reapply them after each load; the `configureState` callback on
`RegisterStateManager` is another place to do this. Journaled grains use the same
`Configure` method on their `IDurableMessageTracker` once per activation.

Handlers keep their payload-and-cursor signature:

```csharp
async ValueTask HandleAsync(OrderSubmitted message, StreamCursor cursor)
{
    if (!state.State.Tracker.TryAcceptMessage(cursor, out var tracker))
        return;

    await state.WriteAsync(state.State with
    {
        Orders = state.State.Orders.Add(message.OrderId),
        Tracker = tracker
    });
}
```

Persist the tracker and business changes in the same write. In `OutboxIdentity`
mode, if publication lands
but removing the sender's outbox item fails, retry carries the same logical
identity even when the provider assigns a new stream position. The retained
receipt suppresses the second effect. A failed receiver commit leaves the event
eligible after recovery. External side effects need their own atomicity or
idempotency contract.

In receipt mode, receipts use provider, complete `StreamId`, sender, epoch, and sequence number.
Timestamp and trace metadata are informational. Unseen lower sequences and
previous epochs remain eligible: different postman groups can deliver sequence 12
before sequence 11. Fan-out to distinct full stream sources is independent.
Equal payloads appended as separate outbox entries have separate identities.

Custom stream callbacks attach identity at the actual send (`using Orleans.Streams`);
receivers still choose their tracking mode:

```csharp
processor.AddPostman<OrderSubmitted>((message, token) =>
    streamProvider.GetStream<OrderSubmitted>(StreamId.Create("orders", message.OrderId))
        .PublishFromOutboxAsync(message, token));
```

For a custom fanout, attach the token once around the awaited publications. The
existing publishing method can keep returning an `IEnumerable<Task>`:

```csharp
processor.AddPostman<SessionUpdatedEvent>(async (message, token) =>
{
    using var scope = RequestContext.AttachOutboxToken(token);
    await Task.WhenAll(PublishSessionUpdateEvent(message));
});
```

`AttachOutboxToken`, `GetOutboxToken`, and `DetachOutboxToken` are C# 14 static
extension members on Orleans' `RequestContext`, imported with `using Orleans.Streams`.
Attachment validates the token and stores the typed `OutboxSequenceToken`. Orleans serializes
it alongside the domain event through the configured provider; participating
endpoints must reference OM and have its generated serializers available.
There is no additional JSON encoding. The token's JSON converter remains available
for storage. `PublishFromOutboxAsync` uses this same scope after routing and projection.

Await every publication before the callback completes. Dispose the scope in the
same logical execution flow; nested scopes restore in reverse order. Disposal
restores only OM's previous entry, including a present null or an absent entry,
and repeated disposal is harmless. Open the scope around iterator enumeration,
not across `yield return` inside the iterator. The same token on distinct providers
or full stream IDs has independent receipts. Publishing distinct events to the
same destination needs separate outbox entries.

Grain calls and asynchronous work started inside the scope also inherit the token.
Keep the scope limited to deliveries of that outbox item. If a called grain publishes
an unrelated event, it inherits the same identity; when both events reach the same
stream, receivers can discard one as a duplicate. Disposal does not revoke context
already captured by that work. A grain method or custom stream observer can read
and remove the incoming token before processing the message:

```csharp
var token = RequestContext.DetachOutboxToken();
await HandleAsync(message, token);
```

`DetachOutboxToken` returns the token and removes its entry from the current logical
flow, preserving unrelated context. It does not create a restore scope. Detach
before starting unrelated publications or grain calls; already-started work keeps
its captured context. Use `GetOutboxToken()` when you want to read without removing.

Both methods return null only when the entry is absent. Present null, wrong-type,
or invalid identity values throw `InvalidOperationException` and leave it unchanged.
Invalid attachment arguments throw `ArgumentException` (`ArgumentNullException`
for null). `StreamManager` detaches before invoking application code; its async
boundary isolates that removal from the publisher. Its handlers continue reading
`cursor.OutboxToken`. Manual stream receivers
must include the token, provider, and complete stream ID in a `StreamCursor` when
using receipt tracking. The bare-token `TryAcceptMessage(token, out tracker)` overload
uses RPC sender high-water ordering, which is different from per-stream receipts.

Generic `AddPostman`, keyed `IPostman<T>`, and the dispatcher do not establish
ambient identity automatically. RPC postmen can pass the token explicitly or opt
in to the same request-context scope.

Ordinary stream publishers without the reserved metadata use provider-position
tracking in both modes. Tokenless events store no position; tagged tokenless events
store a receipt only in `OutboxIdentity` mode. Accepted tokenless events can still
remove expired entries. Present malformed, null,
wrong-type, or legacy string metadata faults the observer before the
application handler and bypasses its normal log-and-swallow error policy.
Missing metadata cannot distinguish a raw publisher from an adapter that dropped
context. There is no strict-identity subscription mode in this version.

### Receipt retention and provider checkpoints

When `RetentionPeriod` is configured, accepting a message removes entries whose
receiver acceptance time is at or before `now - RetentionPeriod`. Entries that
expired are also ignored when deciding whether that message is eligible, so a
duplicate can become eligible again at the cutoff. New acceptance timestamps use
the receiver's clock, never a sender timestamp. Duplicate attempts do not refresh
the window.

Cleanup is part of the returned tracker snapshot or the staged journal operation;
persist it with the business changes. Rejected duplicates return the original
snapshot without cleanup changes. Idle trackers keep their stored state until
another accepted message or explicit eviction; there is no timer or schedule.
An in-memory earliest-expiry bound skips cleanup scans until an entry could expire.
The bound is rebuilt after loading state or manual eviction; a due sweep still
scans the retained entries. Only actual cleanup is recorded in the journal.
Retention bounds history, not the number of messages in that history. There is
no automatic size cap. Without configured retention, receipts grow until explicit
eviction.

Evicting an entry ends its deduplication guarantee. Global and stream eviction
remove checkpoints and receipts according to each entry's own receiver acceptance
time. `EvictOutboxes` and sender-only eviction affect RPC high-water entries only.
`Evict(streamId, cutoff)` targets that full stream across providers;
`Evict(provider, streamId, cutoff)` narrows it to one provider. Namespace eviction
covers all streams in that namespace. Journaling provides the same operations.

`EvictStreamReceipts(cutoff)` removes only receipts, preserving stream checkpoints
and RPC positions. When moving an existing receiver to position tracking, use
`EvictStreamReceipts(DateTimeOffset.MaxValue)` and persist the result to discard
its old receipt history. Selecting position mode alone stops adding receipts but
does not erase previously stored ones.

Native checkpoints remain separate. In receipt mode, accepting an unseen identity
never moves a retained checkpoint backwards. Rejecting a retry returns `false` and the original tracker,
even at a newer native position, so ordinary early-return handlers do not silently
lose an unpersisted checkpoint change. This can cause extra replay after
activation. A maximum checkpoint does not establish safe resumption for an
arbitrarily reordered provider; its ordering/replay contract still applies.

Use `LatestStream(provider, streamId)` or
`LatestStreamSequenceToken(provider, streamId)` for exact checkpoint lookup.
`LatestStream(streamId)` returns no result when several providers match.
Namespace lookups remain for legacy or unambiguous state and return no result
rather than choose between multiple streams.

Use a separate outbox entry for each distinct event on the same destination.
Reusing one token for several payloads on one stream means one logical identity.
The helper publishes one event; it exposes no batch identity API. Provider
aggregation of separate single-event publications is covered by a deterministic
provider serialization and aggregate delivery contract test: two independently
identified Azure Queue containers pass through Orleans' real `BatchContainerBatch`
delivery method and the registered per-item observer. Cluster tests enable
`BatchContainerBatchSize = 8`; they do not prove a live pulling agent formed an
aggregate. A producer batch of distinct
events sharing one context is outside this contract. Raw handlers that bypass
`StreamManager` retain Orleans' normal transitive request-context behavior.

### Upgrading stream tracking

Old binary and JSON snapshots remain readable, with empty receipts. Their
namespace-only checkpoints cannot reveal the original stream key. When no
checkpoint matches the provider and full stream identity, `StreamManager` passes
`null` when attaching or resuming, leaving positioning to Orleans and the provider.
This includes legacy checkpoints and lets an existing application adopt OM without
manufacturing a token or disabling tracked resume. Once the application tracks and
persists a matching cursor, later attachments use its token automatically.

To resume from a known legacy position, bind and persist a verified source mapping
before attaching:

```csharp
var tracker = state.State.Tracker;
var legacy = tracker.LatestStream("events", "orders");
if (legacy is { StreamId: null, Token: not null }
    && tracker.TryAcceptMessage(
        legacy with { ProviderName = "events", StreamId = streamId }, out var rebound))
{
    await state.WriteAsync(state.State with { Tracker = rebound });
}
```

The legacy entry may remain; exact lookup wins for attachment. Without a verified
mapping, attachment proceeds without a token. This does not guarantee replay from
the application's last processed event. Establish a deliberate checkpoint/replay
baseline when that continuity is required. `UseTrackedResumeToken = false` always
omits the token, even after a matching checkpoint becomes available.

Receipts for already processed events cannot be reconstructed. Coordinate the
cutover: pause producers, finish and acknowledge old outboxes, wait for consumers
to commit their catch-up, stop old receiver state writers, upgrade consumers and
persist known source mappings, then upgrade publishers and resume production.
This cannot repair effects duplicated by the old version. Keep existing business
idempotency during the upgrade window if a clean baseline cannot be established.
Mixed-version state writers and downgrade can discard new receipts and are
unsupported without separate migration validation.

### Provider evidence

| Orleans 10.3.1 configuration | Verification |
| --- | --- |
| Default Memory streams | Real-cluster sender acknowledgement failure, reactivation, redelivery, durable receiver reload, and separately appended equal payloads; default body serializer round-trip |
| Default Azure Queue V2 adapter | Provider-owned queue-text encode/decode, consumer serialization, and request-context import |
| Default Event Hubs adapter | Provider-owned event-body encode/decode, cache conversion, consumer serialization, and request-context import |
| Messaging enriched Event Hubs adapter with its default inner container | The same body/cache/consumer path, plus enriched token preservation |

The consumer tests register both the domain-event and Messaging serializers and
verify typed outbox metadata alongside the event. Azure Queue and Event Hubs checks
are serialization contracts, not live-broker delivery or production acceptance.
Custom adapters need their own metadata-preservation proof. Receipt guarantees
require retained state and a
provider/adapter that preserves the reserved entry; they do not provide exactly-once
transport.

### Tracker clock

`MessageTracker` stamps each accepted source with a `Received` time, which
eviction compares against. The clock is not persisted. A tracker uses the first
clock it finds:

1. A clock set on the instance with `RegisterTimeProvider`. Snapshots returned
   by `TryAcceptMessage` and `Evict` keep it.
2. A clock captured by the instance's `Configure` callback.
3. The silo-wide clock from `ConfigureMessageTracker`: `MessageTrackerOptions.TimeProvider`
   when set, otherwise the `TimeProvider` registered in the silo's services.
4. `TimeProvider.System`.

Set the silo-wide clock once instead of registering one on every tracker after
each read. It covers every tracker in the silo, including ones created with
`new MessageTracker()` and ones deserialized from grain state. To use the
`TimeProvider` the silo already registers, call it without setting a clock:

```csharp
siloBuilder.ConfigureMessageTracker(_ => { });
```

Or pick a specific one, such as a keyed domain clock:

```csharp
siloBuilder.ConfigureMessageTracker((options, services) =>
    options.TimeProvider = services.GetRequiredKeyedService<TimeProvider>("pricing"));
```

A tracker cannot reach the silo's services by itself. Without a
`ConfigureMessageTracker` call, there is no silo fallback; an instance without its
own clock uses `TimeProvider.System`.

The silo installs its tracker defaults before any grain activates and removes them when it
stops. Calls add up in registration order, as `services.Configure<MessageTrackerOptions>(...)`
does, and `IServiceCollection` has the same overloads. These global settings are
process-wide, so silos sharing a process, as in an in-process test cluster,
share the defaults of the most recently started configured silo that is still
running. A silo that stops withdraws only its own settings. Use `Configure` for
per-grain settings, or `RegisterTimeProvider` for a clock that takes precedence
over both global and per-instance configuration.

Use `LatestStreamSequenceToken(provider, streamId)` when all you need is the previous
resume token for a complete source. Keep using `LatestStream("prices")` when you need the full
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
| `UseTrackedResumeToken` | `true`                      | Pass the token matching the provider and full stream identity when attaching or resuming, or `null` when none exists. |
| `OnError`               | `null` (log the error)      | Called with the namespace and exception when the handler throws.    |
| `Trace`                 | `MessageTraceOptions.Link`  | How the consumer span relates to the producer's trace.              |
| `TimeProvider`          | registered, else `System`   | Clock for `MessageTraceOptions.ParentWithinLag`.                    |

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

The Event Hubs extensions support Orleans **10.3.1 and 10.4.0** with the same
package binary. On 10.4, enriched tokens share the built-in Event Hubs comparison
domain: equal sequence numbers and event indexes compare equally regardless of
enrichment. Checkpoints still belong to their provider and complete `StreamId`;
this does not make positions from different streams interchangeable. The JSON
discriminators and Orleans serialization aliases remain unchanged.

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

`StreamManager` wraps each delivery in an `orleans.stream.process` consumer
span, except with `None` when nothing is ambient (below). When the token carries
a valid W3C traceparent, such as the one `EnrichedEventHubAdapter` stamps on
publish, each subscription chooses how that span relates to the producer through
`StreamSubscriptionOptions.Trace`:

| `Trace`                                  | Consumer span                                                            |
|------------------------------------------|--------------------------------------------------------------------------|
| `MessageTraceOptions.Link`               | New trace, with an `ActivityLink` to the producer span. The default.     |
| `MessageTraceOptions.Parent`             | Child of the producer span, in the producer's trace. No link.            |
| `MessageTraceOptions.ParentWithinLag(t)` | Child when `|now - enqueued| <= t`, otherwise linked as with `Link`.     |
| `MessageTraceOptions.None`               | Child of the ambient activity with a link; no span when none is ambient. |

`None` joins an existing trace and never starts one: "don't start a trace", not
"never trace". A delivery that already runs inside a trace, such as an in-memory
stream delivered inside the producer's call, gets a span that is a child of the
ambient activity and links to the producer. A delivery with nothing ambient
(`Activity.Current` is `null`), such as one from a persistent stream's pulling
agent, gets no span and no new trace: the handler runs with `Activity.Current`
still `null`, so any spans it starts root their own traces. Choose it to silence
background deliveries and redeliveries while keeping spans inside request-driven
work. The `stream.*` metrics are recorded either way.

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
`ParentWithinLag`, deliveries older than the limit fall back to a link. The lag
is compared by magnitude, so a consumer clock running behind the broker's does
not make an old message look recent. `ParentWithinLag` needs a token that exposes an enqueue time, such as
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

- The Journaling preview now targets Orleans Journaling 10.4.0-alpha.1. Keep
  `AddMessagingJournaling()` and stable keyed component names; use
  `IDurableStateManager.GetOrAddState<TState>` for constructor composition.
  Ordinary `Grain` recovery runs at SetupState; `DurableGrain` remains supported.
  Replace standalone factory `Create` calls with `CreateStandalone`, and custom
  `IJournaledState` implementations with `IStateMachine` (`WritePendingEntries`,
  `WriteSnapshot`, no `DeepCopy`). Use `AddJournaling()` instead of parameterless
  `AddJournalStorage()`. Failed writes/deletes need a fresh activation or manager;
  only failed initial replay can explicitly retry initialization on the same instances.
  Old-preview JSON append/snapshot fixtures replay with unchanged messaging data.
  All OM packages require Orleans 10.4.0; upgrade the host and Orleans packages
  together. The 10.3.1-alpha.1 journal fixtures verify persisted-data recovery,
  not continued support for the old runtime.

- **State managers expose `LastFailureKind` and immutable `Options`.** Custom
  `IStateManager<T>` implementations must add both getters; implementations derived
  from `StateManagerBase<T>` inherit them. Wrappers should forward them to the
  underlying manager. `Options` returns `StateManagerOptionsSnapshot` with the
  effective lifetime `RecoveryPolicy`. `LastFailureKind` reports classified write
  and clear failures under either recovery policy and resets after a successful
  read, write, clear, or save, including recovered success and no-op saves. A recovery
  read which still leaves the mutation throwing retains the failure. Fenced managers
  retain their original classification. Outbox reminder decisions still use the
  separate activation-wide fencing record.

- **Fenced operations throw `StateManagerFencedException`; local inspection remains available.** It still derives from
  `InvalidOperationException`, but exact-type checks should use the dedicated type.
  Inspect `FailureKind` for the existing storage classification and `InnerException`
  for the original failure. `State` and `HasUnsavedChanges` can be read during
  cleanup, but describe the retained local snapshot, not necessarily current storage.
  **Review read-only grain methods:** calls already admitted or interleaved before
  deactivation finishes can now return stale or unsaved values without a fencing
  exception. Do not treat getter availability as proof of a healthy activation or
  durable data. If such reads must fail after a write failure, retain an
  application-level failure guard.
  State assignment, `ReadAsync`, mutations and hook configuration remain fenced;
  the initial failed operation still throws its original exception. Custom state managers now
  have their write/clear classifiers called once under either recovery policy.
  Under fencing, a classifier failure produces `UnknownOutcome` without replacing
  the storage error. Deactivation reasons include state type, operation, and kind.
  The exception uses the stable Orleans alias
  `egil.orleans.messaging.StateManagerFencedException`.
- **Outbox deactivation handles classified fencing.** Unknown outcomes and definite
  non-persistence ensure a recovery reminder; confirmed conflicts skip registration
  and removal, relying on another owner or a subsequent activation without proving
  that owner is alive. `OutboxDeactivationReminderFailed` adds nullable
  `OutboxItemCount`; treat `null` as unknown, not zero. Successful fallback
  registration produces no failure warning. Unfenced, readable empty outboxes retain
  normal idle cleanup. Fencing is recorded on the grain context; a readable empty
  snapshot does not suppress recovery after an uncertain failure. When constructing
  a manager directly inside a grain, pass its grain context so deactivation and this
  recovery handoff are available. Injected and registered managers already do so.

- **Stream tracking now defaults to provider positions, including outbox-tagged deliveries.**
  Receivers must be idempotent for republished messages at new provider positions.
  To preserve the previous receipt behavior, set
  `StreamTrackingMode = StreamTrackingMode.OutboxIdentity` through
  `ConfigureMessageTracker` globally or `Tracker.Configure` in the grain's state
  configuration hook. `RetentionPeriod` enables cleanup on acceptance; `null`
  preserves manual eviction. Use `EvictStreamReceipts(DateTimeOffset.MaxValue)`
  and persist the result when discarding old receipts without losing checkpoints.
  Snapshot formats remain readable. New journal operations record retention
  cutoffs and receipt-only eviction; do not downgrade journal readers or writers
  after using these operations.

- **Outbox request-context metadata is now a typed `OutboxSequenceToken`.** Use
  `RequestContext.AttachOutboxToken(token)` for scoped fanout or grain calls and
  `RequestContext.GetOutboxToken()` for manual reception (`using Orleans.Streams`,
  C# 14). Use `RequestContext.DetachOutboxToken()` to read and remove the token before
  unrelated downstream work. Participating endpoints must
  have OM's generated serializers available.
  The former `v1:` JSON string is no longer accepted.
  Persisted tokens and receipts keep their existing formats.

- **Rename `OutboxReminderPolicy.OnRetry` to `OnDeactivation` in configuration.**
  The default policy now uses only grain timers while active and registers a
  reminder only during orderly deactivation with pending work. Failed registration
  still logs `OutboxDeactivationReminderFailed`. Abrupt crashes can bypass this
  handoff; choose `KeepRegistered` when a pre-established fallback is needed.
- **Reminder periods are separate from timer retries.** `RetryDelay` controls grain
  timers only. Configure `ActiveReminderPeriod` (default five minutes) for pending
  work recovery and `IdleReminderPeriod` (default one hour) for KeepRegistered's
  idle fallback. Both must be at least one minute. Keep the idle period above your
  grain's collection age with a collection/deactivation margin. KeepRegistered
  starts registration during activation and removes the fallback during empty
  deactivation. Persist deferred acknowledgements before that cleanup. Inherited
  reminders are looked up only after firing when removal needs a handle;
  registration directly upserts and may reset cadence.
- **KeepRegistered requires an established fallback.** Attaching the processor in
  the constructor only installs the processor and its lifecycle hook; it performs
  no reminder API calls or asynchronous registration. During activation, Orleans
  awaits the hook's asynchronous reminder registration. Failure or cancellation
  intentionally fails grain activation, so grain calls cannot proceed without the
  promised fallback. For attachment in `OnActivateAsync`
  or later, await a post before business writes to confirm registration; asynchronous
  initialization failures are logged. Activation uses the active reminder period
  when persisted work is pending. Once established, failed period adjustments log a
  warning without failing posts, since the fallback remains registered.

- If `RegisterOutboxProcessor` runs in `OnActivateAsync` or a grain method, add
  `siloBuilder.ConfigureOutboxProcessor()` to host setup (an existing options
  overload also suffices). This installs the automatic deactivation safeguard
  before Orleans starts the grain lifecycle. Missing setup now throws during
  processor registration. Constructor registration needs no extra host setup.

- **Stream tracking now uses complete source identity.** `StreamCursor` adds
  `StreamId` and `OutboxToken`; existing constructor arguments keep their meaning.
  `StreamManager` always supplies the full source. Use provider-qualified full-id
  lookups for resume. `LatestStream(StreamId)` and `Evict(StreamId, cutoff)` now
  match the actual key rather than every key in its namespace. Missing or legacy
  namespace-only checkpoints now allow attachment without a token. Rebind verified
  legacy checkpoints when resuming from their position is required, following
  [Upgrading stream tracking](#upgrading-stream-tracking).
- **Outbox stream publication now carries logical identity automatically through
  `AddStreamPostman`.** Custom stream postmen use `PublishFromOutboxAsync` or an
  explicit `AttachOutboxToken` scope; generic and RPC postmen remain context-free
  unless they opt in. Consumers must persist exact receipts with
  business changes and retain them until deliberate eviction. Upgrade consumers
  before publishers after a coordinated drain/catch-up baseline. Old state writers
  can drop the new data. See [Receiver Dedup](#receiver-dedup).

- **State recovery now defaults to `FenceAndDeactivate`.** To retain the previous
  behavior, explicitly set `RecoveryPolicy = StateRecoveryPolicy.ReadBack` globally,
  on a factory registration, or on an individual manager. Direct construction can
  pass `recoveryPolicy: StateRecoveryPolicy.ReadBack`.
- Custom `IStateManagerFactory.Create<T>` implementations must accept
  `StateManagerOptions options` after `createInitialState`, then the existing optional
  `configureState`, then `IGrainContext? grainContext = null`. Forward
  `options.RecoveryPolicy` and `grainContext` to the manager constructor. Factories
  remain stateless; registration resolves a fresh snapshot before invoking them.
- `RegisterStateManagerAsync` now accepts `configure` before `cancellationToken`.
  Change positional token arguments to `cancellationToken: token` (or supply the
  new callback argument). Synchronous registration and factory helpers append the
  optional callback. `IStateManager<T>` gains no members.

- Replace `AddGrainPostman<TSub, TGrain>(resolveGrain, call)` with
  `AddPostman<TSub>(handler)`. Resolve and invoke the grain in one handler;
  remove the `TGrain` type argument. The payload is required, while delivery
  token, grain factory, and cancellation are independently optional, in that
  order. All eight shapes support both `Task` and `ValueTask`.

  ```diff
  - .AddGrainPostman<OrderSubmitted, IOrderProjectionGrain>(
  -     (message, grains) => grains.GetGrain<IOrderProjectionGrain>(message.OrderId),
  -     (grain, message, token) => grain.ApplyAsync(message, token))
  + .AddPostman<OrderSubmitted>((message, token, grains) =>
  +     grains.GetGrain<IOrderProjectionGrain>(message.OrderId).ApplyAsync(message, token))
  ```

  On C# 13+, the priorities in the handler table above preserve existing
  token-based defaults when a lambda fits more than one shape. Explicitly type
  the handler when a different interpretation is intended. Older compilers may
  require explicit parameter and return types for newly ambiguous calls.

- `RegisterOutboxProcessor` takes the outbox accessor and a `configure`
  callback instead of an `OutboxProcessorOptions<T>` instance.
  `OutboxProcessorOptions<T>.OutboxAccessor` is gone; pass the accessor as the
  first argument. The scheduling settings moved to a non-generic
  `OutboxProcessorOptions` base class, which `siloBuilder.ConfigureOutboxProcessor(...)`
  sets for every processor in the silo.

  ```diff
  - this.RegisterOutboxProcessor(new OutboxProcessorOptions<IOrderEvent>
  - {
  -     OutboxAccessor = () => state.State.Outbox,
  -     AcknowledgePosted = RemovePosted,
  -     RetryDelay = TimeSpan.FromMinutes(1),
  - })
  + this.RegisterOutboxProcessor(() => state.State.Outbox, options =>
  + {
  +     options.AcknowledgePosted = RemovePosted;
  +     options.RetryDelay = TimeSpan.FromMinutes(1);
  + })
  ```

  When the accessor returns a collection expression, give the lambda an explicit
  return type so the payload type can be inferred: `static Outbox<string> () => []`.

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
  Custom factories must accept and forward the resolved `StateManagerOptions` and
  optional `IGrainContext` described above. Configure hooks in the constructor for injected or
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
- Register payload subtypes with `AddPostman` and `AddStreamPostman`. Replace `AddPostmanWithToken` with `AddPostman`; choose the payload and optional delivery token, grain factory, and cancellation arguments your handler needs. Both `Task` and `ValueTask` are supported, with the overload priorities described above.
- Supply state factories for types without a public parameterless constructor. Custom `IStateManagerFactory` implementations receive storage, the initial-state factory, resolved `StateManagerOptions`, optional runtime configuration, and optional `IGrainContext`. Forward the recovery policy and grain context to each manager.
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

The multi-stream `AddStreamPostman` overloads introduce one narrow source ambiguity:
a selector returning an untyped `default`, such as `message => default`, can now
match either `StreamId` or `IEnumerable<StreamId>`. Specify `default(StreamId)` to
retain single-stream selection, or return `[]` for an intentional multi-stream no-op.
