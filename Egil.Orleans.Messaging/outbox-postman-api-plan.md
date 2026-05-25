# Outbox Postman API Plan

## Goal

Make `OutboxProcessor<TOutbox>` less dependent on inline grain lambdas by
introducing reusable postman services. Grains should declare which postmen
handle which outbox message types, while delivery implementation lives in DI
services that can be tested outside Orleans.

## Core Decisions

- Add `IPostman<TMessage>` as the delivery abstraction.
- Keep current `AddPostman(...)` lambda overloads for local/simple cases.
- Add `OutboxProcessor<TOutbox>.AddPostman<TMessage>(string postmanName)` to
  resolve keyed `IPostman<TMessage>` from the grain activation service
  provider.
- Use the grain-scoped service provider. `OutboxProcessor` must not create or
  dispose child scopes.
- Register postmen through explicit outbox APIs named `AddOutboxPostman`, not
  generic `AddPostman` service-registration APIs.
- Allow one concrete postman to implement multiple `IPostman<TMessage>`
  interfaces and register all of them with one call.
- Prefer attribute-based postman names for discovery and one-line
  registration.
- Keep `OutboxProcessor` responsible for orchestration, timeout/cancellation,
  retry scheduling, acknowledgements, and telemetry.
- Keep `IPostman<TMessage>` responsible only for delivery work under the
  processor-owned cancellation budget.

## Proposed Public API

```csharp
public interface IPostman<in TMessage>
    where TMessage : notnull
{
    ValueTask PostAsync(
        TMessage message,
        CancellationToken cancellationToken);
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class OutboxPostmanAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
```

```csharp
public sealed partial class OutboxProcessor<TOutbox>
    where TOutbox : notnull
{
    public OutboxProcessor<TOutbox> AddPostman<TMessage>(
        string postmanName)
        where TMessage : TOutbox;
}
```

```csharp
namespace Microsoft.Extensions.DependencyInjection;

public static class OutboxPostmanServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddOutboxPostman<TPostman>()
            where TPostman : class;

        public IServiceCollection AddOutboxPostman<TPostman>(
            string postmanName)
            where TPostman : class;

        public IServiceCollection AddOutboxPostman<TMessage, TPostman>(
            string postmanName)
            where TMessage : notnull
            where TPostman : class, IPostman<TMessage>;
    }
}
```

## Registration Behavior

`AddOutboxPostman<TPostman>()`:

- Reads `[OutboxPostman("name")]` from `TPostman`.
- Finds all closed `IPostman<TMessage>` interfaces implemented by `TPostman`.
- Registers `TPostman` as scoped.
- Registers every discovered `IPostman<TMessage>` as keyed scoped using the
  attribute name.
- Throws if `TPostman` does not implement any closed `IPostman<TMessage>`.
- Throws if no attribute exists and no explicit name was provided.
- Rejects open generic postman types in v1.

Important lifetime detail:

```csharp
services.AddScoped<TPostman>();

services.AddKeyedScoped(
    typeof(IPostman<>).MakeGenericType(messageType),
    postmanName,
    (sp, _) => sp.GetRequiredService<TPostman>());
```

This lets one concrete scoped postman instance satisfy multiple message
contracts during one grain activation.

## Usage Examples

```csharp
[OutboxPostman("eventhub")]
public sealed class EventHubOrderPostman :
    IPostman<OrderPlaced>,
    IPostman<OrderCancelled>
{
    public ValueTask PostAsync(OrderPlaced message, CancellationToken ct)
    {
        // Send to Event Hubs.
    }

    public ValueTask PostAsync(OrderCancelled message, CancellationToken ct)
    {
        // Send to Event Hubs.
    }
}
```

```csharp
services.AddOutboxPostman<EventHubOrderPostman>();
```

```csharp
outboxProcessor = this.RegisterOutboxProcessor(options)
    .AddPostman<OrderPlaced>("eventhub")
    .AddPostman<OrderCancelled>("eventhub");
```

## Postman Execution Contract

`OutboxProcessor` owns delivery budget. A postman may retry internally, use SDK
retry policies, or perform target-specific backoff, but it must:

- Pass the cancellation token to async SDK/API calls.
- Stop promptly when cancellation is requested.
- Let `OperationCanceledException` escape.
- Avoid acknowledging or removing outbox items.
- Avoid mutating grain state.
- Be idempotent when the external target can receive duplicate attempts.

Processor behavior:

- Successful `PostAsync` call marks the item as posted.
- Exceptions are treated as failed delivery and reconciled through existing
  retry behavior.
- Cancellation/timeout remains processor-owned and leaves the item pending.
- Processor telemetry measures total postman call duration.

## Built-In Postman Helpers

Convenience APIs implemented on top of `AddPostmanCore`.

Stream postman:

```csharp
public OutboxProcessor<TOutbox> AddStreamPostman<TMessage>(
    string streamProviderName,
    Func<TMessage, StreamId> streamId)
    where TMessage : TOutbox;

public OutboxProcessor<TOutbox> AddStreamPostman<TMessage, TEvent>(
    string streamProviderName,
    Func<TMessage, StreamId> streamId,
    Func<TMessage, TEvent> project)
    where TMessage : TOutbox;
```

Grain postman:

```csharp
public OutboxProcessor<TOutbox> AddGrainPostman<TMessage, TGrain>(
    Func<TMessage, IGrainFactory, TGrain> resolveGrain,
    Func<TGrain, TMessage, Task> call)
    where TMessage : TOutbox
    where TGrain : IGrain;
```

These helpers are part of the completed postman API surface. They keep
common Orleans stream and grain fan-out cases on the same processor-owned
dispatch, retry, timeout, acknowledgement, reconciliation, and telemetry path.

## Tests

- `AddOutboxPostman<TPostman>()` registers all implemented
  `IPostman<TMessage>` contracts.
- Multiple `IPostman<TMessage>` contracts on one concrete postman resolve to
  one scoped concrete instance.
- Missing `[OutboxPostman]` without explicit name throws.
- Empty explicit names, empty attribute names, and open generic postman types
  throw.
- Concrete type with no `IPostman<TMessage>` contracts throws.
- `OutboxProcessor.AddPostman<TMessage>("name")` resolves keyed postman from
  grain activation services.
- Successful keyed postman call acknowledges item.
- Throwing keyed postman goes through existing retry/reconcile path.
- Cancellation from processor propagates to postman and leaves item pending.
- Stream postman helpers publish to the selected Orleans stream and
  acknowledge on success.
- Projected stream postman helpers publish projected events and acknowledge on
  success.
- Grain postman helpers resolve a grain through `IGrainFactory`, invoke it, and
  acknowledge on success.
- Existing lambda-based `AddPostman(...)` behavior remains unchanged.

## Implementation Steps

1. Add the core public contract:
   - `IPostman<TMessage>`
   - `OutboxPostmanAttribute`

2. Add service-registration APIs:
   - `IServiceCollection.AddOutboxPostman<TPostman>()`
   - `IServiceCollection.AddOutboxPostman<TPostman>(string postmanName)`
   - `IServiceCollection.AddOutboxPostman<TMessage, TPostman>(string postmanName)`
   - Validate missing attributes, missing postman contracts, empty names, and open generic postman types.

3. Add keyed processor registration:
   - `OutboxProcessor<TOutbox>.AddPostman<TMessage>(string postmanName)`
   - Resolve from the grain activation service provider using keyed DI.
   - Reuse the existing `AddPostmanCore` dispatch pipeline so retry, timeout, acknowledgement, reconciliation, and telemetry stay unchanged.

4. Add focused registration tests:
   - Attribute-based registration discovers all implemented contracts.
   - Multiple contracts resolve to the same scoped concrete postman instance.
   - Explicit-name registration works without an attribute.
   - Missing attribute and missing postman contracts throw.

5. Add Orleans processor behavior tests:
   - Keyed postman success acknowledges and removes pending items.
   - Keyed postman exceptions flow through existing failure reconciliation.
   - Existing lambda postman tests remain green.

6. Add cancellation/timeout tests:
   - Processor cancellation propagates to keyed postman.
   - Timeout leaves the pending item unacknowledged.

7. Update user documentation:
   - README examples for service postmen.
   - API design section for the postman service contract.

8. Add built-in helper APIs:
   - `OutboxProcessor<TOutbox>.AddStreamPostman<TMessage>(...)`
   - `OutboxProcessor<TOutbox>.AddStreamPostman<TMessage, TEvent>(...)`
   - `OutboxProcessor<TOutbox>.AddGrainPostman<TMessage, TGrain>(...)`

9. Add helper behavior tests:
   - Stream helper publishes the outbox item and acknowledges.
   - Projected stream helper publishes the projected event and acknowledges.
   - Grain helper resolves the target grain, invokes it, and acknowledges.

## Implementation Status

- Steps 1-9 are implemented.
