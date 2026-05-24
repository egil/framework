# Good and bad tests

## Good tests

Behavior-focused tests through public interfaces.

```csharp
[Fact]
public async Task Order_can_be_placed_with_valid_input()
{
    var order = OrderBuilder.Create().WithItems([item]).Build();

    var result = await service.PlaceAsync(order);

    Assert.Equal(OrderStatus.Confirmed, result.Status);
}
```

## Bad tests

- Verifying internals or call counts.
- Naming by implementation detail.
- Asserting on private storage or setup side effects.

## AAA at same abstraction level

If arrange is low-level while act/assert are business-level, lift setup into a helper.

## Naming

Scenario sentence with underscores.

## Verify (snapshot tests)

Use snapshots for contract-like outputs (serialized wire formats, API specs, full artifact captures). Default to explicit assertions elsewhere.

## Pruning

Drop scaffolding tests whose behavior is covered by higher-level integration tests.

## Non-deterministic waits

Avoid `Task.Delay` and `Thread.Sleep`. If you must wait for non-owned clocks, isolate and document.
