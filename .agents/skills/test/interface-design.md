# Interface design for testability

Good interfaces make tests easier and often improve production composition.

## Principles

1. **Accept dependencies, don’t instantiate them**.
2. **Return values where possible** rather than hidden side effects.
3. **Keep interfaces small**: fewer methods, fewer parameters.
4. **Move complexity behind factories/helpers**.

## Deep module pattern

Prefer a small interface that hides implementation complexity.

- Fewer methods ⇒ fewer mock/fake permutations.
- Simpler parameters ⇒ clearer test setup.

## SDK-style interfaces

Prefer specific methods over generic dispatch.

```csharp
public interface IInventoryClient
{
    Task<IReadOnlyList<Product>> SearchAsync(string query, CancellationToken ct);
    Task<StockLevel> GetStockAsync(ProductId id, CancellationToken ct);
}

public interface IGenericClient
{
    Task<TResponse> SendAsync<TRequest, TResponse>(string route, TRequest request);
}
```

The specific interface is easier to fake and reason about.

## Typed IDs

Prefer typed IDs where your domain model defines them; they make wrong-entity mistakes harder and make tests clearer.
