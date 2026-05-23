# Fakes

We use **hand-built fakes**, not mock libraries. Fakes are reusable, encode the PE domain language, and survive interface refactors that would shatter mock setups.

## Where to fake

Fake at **system boundaries** that make tests non-deterministic, slow, or hard to set up:

- External HTTP clients (CPMS, WaaS, OCPI).
- Blob / table storage.
- Event Hub / Service Bus publishers.
- Time (`ManualTimeProvider` from `TimeProviderExtensions`).
- Randomness.

Do **not** fake your own domain types or internal collaborators. The Orleans test silo, in-memory grain storage, and in-memory streams *are* the integration boundary — let real grains call real grains.

## Mocks are a red flag

Mock libraries (NSubstitute, Moq, FakeItEasy) are strongly discouraged in new code. Hand-built fakes are the default; if you reach for a mock library, state which justification from [SKILL.md](SKILL.md#properties-to-aim-for) applies (lower prod quality, harder-to-maintain test, slow, non-deterministic) — "it's quicker to write" is not a justification.

**Sole legacy zone: the entire `Clever.PricingEngine.Client.Tests` project.** Originally written by an external team using AutoFixture + NSubstitute via the `[AutoNSubstituteData]` and `[InlineAutoNSubstituteData]` attributes. New tests *inside that project* may continue the pattern for consistency with surrounding code. New tests *anywhere else* should use hand-built fakes. Do not propagate `AutoNSubstituteData` to other projects, and do not "fix" the existing usages in Client.Tests as a drive-by.

Why fakes win:

- Encode real behavior — catch contract drift; mocks freeze the contract at setup time.
- Reusable across tests — one `FakeCpmsClient` serves dozens of tests.
- Speak the domain — `fake.WithEvse(...)` reads better than `mock.Setup(x => x.GetTariffsFromEvseIds(...)).Returns(...)`.
- Survive refactors — adding a new interface member only requires one stub in the fake (see [commit-discipline.md](commit-discipline.md) compile-required-stub exception).

## Where fakes live

| Used by | Location |
|---|---|
| 2+ test classes (the default) | `test/Clever.PricingEngine.TestingUtils/Fakes/` |
| Exactly one test class | private nested class in that test file |

Lift inline fakes into `TestingUtils/Fakes/` as soon as a second test needs them.

## Naming

`Fake<RealTypeName>` — mirrors the production interface or class. Examples:

- `FakeCpmsClient` ← `ICpmsClient`
- `FakeSpotPriceStorage` ← `ISpotPriceStorage`
- `FakeTariffPublishingService` ← `ITariffPublishingService`
- `FakeLocationGrain` ← `ILocationGrain`

## Fake design

- Implement the real interface fully.
- Mimic real behavior, not just return canned values — e.g., `FakeSpotPriceStorage` actually stores and retrieves.
- Expose state as properties for assertion (`fake.Evses`, `fake.PublishedRecords`).
- Use the same `ManualTimeProvider` instance the test uses — pass it in via constructor or shared fixture.
- Default to sensible empty state; let the test add what it needs.

Reference example: [`FakeCpmsClient`](../../../test/Clever.PricingEngine.TestingUtils/Fakes/FakeCpmsClient.cs) — exposes `Evses` for setup, `CreateFallbackTariffs` flag for the alternate path, real `EvseId` filtering logic.

## In the silo fixture

`PricingEngineSiloFixture` registers fakes via DI for the in-process silo. To add a new fake:

1. Build the fake in `TestingUtils/Fakes/`.
2. Add a property on the fixture (`public FakeFooService FooService { get; }`).
3. Register it in `InitializeAsync`'s `ConfigureSilo` callback.
4. Tests access it via `fixture.FooService` and assert against its captured state.
