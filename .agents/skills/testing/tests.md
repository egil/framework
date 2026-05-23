# Good and bad tests

## Good tests

Integration-style. Test through real interfaces. Read like a specification.

```csharp
[Fact]
public async Task User_can_checkout_with_valid_cart()
{
    var cart = CartBuilder.Create(timeProvider).WithItem(product).Build();

    var result = await checkout.CompleteAsync(cart, paymentMethod);

    Assert.Equal(CheckoutStatus.Confirmed, result.Status);
}
```

Characteristics:

- Tests behavior callers care about.
- Uses public API only.
- Survives internal refactors.
- Describes WHAT, not HOW.
- One logical assertion per test (multiple `Assert` calls are fine if they verify one behavior).

## Bad tests

Coupled to internal structure.

```csharp
// BAD: tests implementation details
[Fact]
public async Task Checkout_calls_payment_service_process()
{
    var mockPayment = Substitute.For<IPaymentService>();
    await checkout.CompleteAsync(cart, paymentMethod);
    await mockPayment.Received().ProcessAsync(cart.Total);
}
```

Red flags:

- Mocking internal collaborators (mocks are banned anyway — see [fakes.md](fakes.md)).
- Asserting on call counts / order.
- Test breaks when refactoring without behavior change.
- Test name describes HOW not WHAT.
- Verifying through external means (e.g., reading storage directly) instead of the public interface.

```csharp
// BAD: bypasses interface to verify
[Fact]
public async Task CreateUser_saves_to_database()
{
    await users.CreateAsync(new User("Alice"));
    var state = await fixture.ReadGrainState<UserState>(stateName, grainId);
    Assert.NotNull(state);
}

// GOOD: verifies through interface
[Fact]
public async Task Created_user_is_retrievable()
{
    var user = await users.CreateAsync(new User("Alice"));
    var retrieved = await users.GetAsync(user.Id);
    Assert.Equal("Alice", retrieved.Name);
}
```

## AAA at the same abstraction level

A test should communicate everything the reader needs. If Arrange has many small detailed steps but Act and Assert work at a high level, **the Arrange step needs refactoring** — usually by lifting setup into a builder method or a local factory in the test class.

```csharp
// BAD: low-level Arrange, high-level Act/Assert
var rev = new GroupRevision { ... 30 properties ... };
rev.Periods.Add(new Period { Start = ..., End = ..., Price = ... });
rev.Periods.Add(new Period { Start = ..., End = ..., Price = ... });
await groupManager.AddAndApproveRevisionAsync(rev);

await SendUsage();
Assert.Equal(50m, await GetTotalPrice());

// GOOD: same abstraction throughout
var rev = GroupRevisionBuilder
    .Create(timeProvider)
    .WithTimeOfDayPeriods(count: 2, peakPrice: 50m)
    .Build();
await groupManager.AddAndApproveRevisionAsync(rev);

await SendUsage();
Assert.Equal(50m, await GetTotalPrice());
```

## Naming

Scenario sentence with underscores: `Add_item_to_empty_shopping_cart`, `Tariff_uses_spot_price_when_window_open`. Not `TestAdd1`, not `Should_work`.

## Verify (snapshot tests)

Use sparingly. Verify is appropriate for:

- Wire-format / serialization compatibility (e.g. `WireFormatBackwardCompatibilityTests`, `TariffSerializationTests`) — the snapshot *is* the contract.
- Generated API specs (e.g. `TariffsOpenApiTests`, `PricingRulesOpenApiTests`).
- Whole-grain-state replay debugging (`ReplayChargingSessionTests`, `ReplayLocationMessageTests`) where the snapshot is the entire investigated artifact.
- Other large outputs that must match wholesale and where hand-written assertions would be unreadable.

**Default to explicit assertions.** Snapshots hide intent — a reader can't tell *which* part of the snapshot is the behavior under test. The repo has over-used Verify in earlier phases; new tests should justify it.

When using Verify: `dotnet verify review` / `dotnet verify accept` to manage snapshots.

## Pruning

Don't keep low-level scaffolding tests once higher-level integration tests cover the behavior:

- Asserting an enum value exists.
- Asserting a type is registered in DI.
- Asserting a property has a getter.

Delete them. They cost maintenance and prove nothing the integration tests don't.

## Wall-clock exception

`Task.Delay` / `Thread.Sleep` are a red flag in tests. The only justified deviation is when the SUT's behavior depends on a clock you don't own (e.g. `Activity.Duration` uses `Stopwatch`, GC timing, OS scheduler) — in those cases the test would otherwise be non-deterministic, which is the explicit justification.

If a test must wait for `Activity.Duration`, add a comment explaining that `Activity` uses `Stopwatch` internally and cannot use the app's `TimeProvider`.

For anything time-based that you *do* own — domain code, grain timers, fakes — inject `TimeProvider` and use `ManualTimeProvider`.
