# Builders

Fluent builders make tests **readable**. They hide the irrelevant and surface the essential. The goal: a test should communicate, on its own, everything the reader needs to understand it.

## When to use a builder

| Situation | Use |
|---|---|
| Type has 1–2 trivial params (e.g., `new Money(100, "EUR")`) | raw constructor |
| Type has many fields, most irrelevant per test | builder |
| Same shape recurs across 2+ tests | builder method |
| Setup is core to *what the test is testing* | inline in test, named clearly |
| Setup is incidental but required for compile / valid state | builder defaults |

## Pattern

Match the existing convention (see `Clever.PricingEngine.TestingUtils/Builders/Tariffs/TariffBuilder.cs`):

```csharp
public class TariffBuilder
{
    private string countryCode;
    // ... other fields with private defaults ...

    private TariffBuilder(TimeProvider timeProvider)
    {
        var utcNow = timeProvider.GetUtcNow();
        // sensible defaults that produce a valid object
        countryCode = "DK";
        partyId = "CLE";
        // ...
    }

    public static TariffBuilder Create(TimeProvider timeProvider) => new(timeProvider);

    public TariffBuilder WithCountryCode(string value) { countryCode = value; return this; }

    public Tariff Build() => new(countryCode, partyId, ...);
}
```

Conventions:

- `static Create(TimeProvider)` factory entry — never `new` the builder directly.
- All defaults set in the constructor — `Build()` always produces a valid object.
- `WithX(...)` returns `this` for chaining.
- Terminal `Build()` returns the domain type.

## High-level methods, not just `WithX`

A naive builder mirrors every property: `WithFoo`, `WithBar`, `WithBaz`. That helps a little. The real value comes from **business-language methods** that compose multiple low-level fields:

```csharp
// LOW VALUE
TariffBuilder.Create(time)
    .WithElements([
        new TariffElement(...),
        new TariffElement(...),
        new TariffElement(...),
    ])
    .Build();

// HIGH VALUE — reads at the abstraction the test cares about
TariffBuilder.Create(time)
    .WithTimeOfDayPeriods(count: 3, peakPrice: 50m)
    .Build();
```

Add a high-level method to the builder when:

- Two or more tests build the same shape.
- A property combination only makes sense as a unit (e.g., a period needs both start and end).
- The raw `WithX` chain hides the test's intent.

## Folder layout

`test/Clever.PricingEngine.TestingUtils/Builders/<DomainArea>/<X>Builder.cs` — domain area mirrors source namespaces:

```
Builders/
├── ChargingSessions/
├── Evses/
├── Groups/
├── IdTags/
├── Locations/
├── SessionMessages/
└── Tariffs/
```

## Two categories

Same convention applies to both:

1. **PE domain types** — `TariffBuilder`, `GroupRevisionBuilder`, `EvseBuilder`. Used by tests of code that *produces* or *consumes* these.
2. **Inbound external payloads** — `LocationMessageBuilder`, `InstallationHistoryUpdatedBuilder`, session-message builders. Used by tests of code that *processes* third-party events.

No separate folder for category 2 — group by domain area regardless of source.

## Abstract base for families

When a family of payloads share scaffolding, factor a base class — see `SessionMessagePayloadBuilderBase<TPayload>` and the session-message builders that derive from it.
