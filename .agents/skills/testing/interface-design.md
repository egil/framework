# Interface design for testability

Good interfaces make testing natural. The same properties also tend to make production code easier to compose, evolve, and reason about.

## 1. Accept dependencies, don't create them

```csharp
// Testable
public class OrderProcessor
{
    public OrderProcessor(IPaymentGateway gateway, TimeProvider time) { ... }
}

// Hard to test
public class OrderProcessor
{
    private readonly StripeGateway gateway = new(Environment.GetEnvironmentVariable("STRIPE_KEY")!);
}
```

DI also makes the boundary obvious — anything injected is a candidate for a fake (see [fakes.md](fakes.md)).

## 2. Return results, don't produce side effects

```csharp
// Testable
public Discount CalculateDiscount(Cart cart) { ... }

// Hard to test — must inspect cart afterwards
public void ApplyDiscount(Cart cart) { cart.Total -= ...; }
```

Pure-ish functions assert directly on the return value. Side-effecting code forces tests to go fishing.

## 3. Small surface area

- Fewer methods → fewer tests needed.
- Fewer parameters → simpler test setup.
- Hide construction complexity behind factories or builders.

## Deep modules

From "A Philosophy of Software Design":

**Deep module** = small interface + lots of implementation.

```
┌─────────────────────┐
│   Small Interface   │  ← Few methods, simple params
├─────────────────────┤
│  Deep Implementation│  ← Complex logic hidden
└─────────────────────┘
```

**Shallow module** = large interface + little implementation. Avoid.

```
┌─────────────────────────────────┐
│       Large Interface           │  ← Many methods, complex params
├─────────────────────────────────┤
│  Thin Implementation            │  ← Just passes through
└─────────────────────────────────┘
```

When designing an interface, ask:

- Can I reduce the number of methods?
- Can I simplify the parameters?
- Can I hide more complexity inside?

Deep modules pay off twice: production gets a clean abstraction, tests get a small surface to verify.

## SDK-style over generic dispatchers

Prefer specific operations over generic plumbing.

```csharp
// GOOD: each method is independently fakeable, type-safe
public interface ICpmsClient
{
    Task<ImmutableArray<Tariff>> GetTariffsFromEvseIds(...);
    Task<Evse> GetEvseAsync(EvseId id, CancellationToken ct);
}

// BAD: fake needs conditional logic to dispatch on endpoint
public interface IGenericClient
{
    Task<TResponse> SendAsync<TRequest, TResponse>(string endpoint, TRequest req);
}
```

The SDK approach means each fake method returns one specific shape; no `if (endpoint == "/tariffs")` branching in test setup.

## Typed IDs

Use `LocationId`, `EvseId`, `SessionId` — never raw `string` or `Guid` for entity references in either prod or tests. The compiler catches confusion (you can't pass an `EvseId` where a `LocationId` is expected); tests read at the domain level.
