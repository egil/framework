# Builders

Fluent builders should make tests read like a story.

## Use a builder when

| Situation | Use |
|---|---|
| 1–2 trivial parameters | raw constructor |
| Many fields and most are irrelevant per test | builder |
| Same setup pattern appears in 2+ tests | dedicated builder method |
| Setup is core behavior under test | explicit setup in test |
| Setup is incidental | builder defaults |

## Pattern

```csharp
public class OrderBuilder
{
    private string customerId;
    private int quantity;

    private OrderBuilder()
    {
        customerId = "customer-1";
        quantity = 1;
    }

    public static OrderBuilder Create() => new();

    public OrderBuilder WithCustomerId(string value) { customerId = value; return this; }
    public OrderBuilder WithQuantity(int value) { quantity = value; return this; }

    public Order Build() => new(customerId, quantity);
}
```

Conventions:

- `static Create(...)` factory entry point.
- Defaults in the constructor so `Build()` always returns valid objects.
- `WithX(...)` methods return `this`.
- `Build()` returns the domain model.

## Prefer domain-language methods over raw WithX

```csharp
// LOW VALUE
OrderBuilder.Create().WithItemName("A").WithItemName("B").Build();

// HIGH VALUE
OrderBuilder.Create().WithTwoBasicItems().Build();
```

Add convenience methods when:

- The shape appears in multiple tests.
- Several properties logically belong together.
- The raw chain hides the scenario intent.

## Folder layout

Use the local test project conventions. Typical layout:

- `test/<Project>.Tests/Builders/<DomainArea>/<X>Builder.cs`
- `test/<Project>.Tests/Builders/<DomainArea>/BaseBuilder.cs` (for shared setup)
