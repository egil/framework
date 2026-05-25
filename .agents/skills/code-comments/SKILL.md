---
name: code-comments
description: Use when adding, editing, or reviewing source code comments. Guides Codex to write useful comments that explain why code is shaped a certain way, especially around non-obvious constraints, concurrency hazards, Orleans lifecycle rules, stream/outbox ordering, compatibility requirements, parsing loosely structured input, external standards, and workarounds.
---

# Code Comments

- Err on the side of over-commenting when the reasoning is not obvious. Comments should explain **WHY** code is written a particular way; the **WHY** is the most important part.
- Do comment non-obvious implementation details: concurrency hazards, Orleans grain lifecycle constraints, stream/outbox ordering guarantees, compatibility requirements (public HTTP APIs, Event Hub payloads, Contracts/Client NuGet -- see [compatibility-governance](../compatibility-governance/SKILL.md)), upstream workarounds, and intentional deviations from the obvious helper or API.
- When parsing strings, logs, Event Hub payloads, CPMS/WAAS messages, or other loosely structured data, include a comment with an example of the raw format being parsed. Show edge cases, optional fields, or malformed-but-observed inputs when they affect the parser.
- When code follows an external standard or protocol (OCPI, OCPP, ISO-15118, ISO-3166, etc.), include a link to the relevant source so future readers can verify the rule.
- Do not add comments that simply narrate clear code, such as `// set the timeout` immediately before assigning a timeout.
- Keep workaround comments next to the workaround. Add a `TODO:` describing the condition for removing it, and link an issue when the workaround is tied to an upstream bug.

Good comments explain the constraint or tradeoff:

```csharp
// Workaround for sessions where started is later than completed/tariff end.
// TODO: Can be removed once started is guaranteed to always be less than or equal to completed/tariff end.
var subscriptionSearchPeriod = Schedule.TryCreate(sessionRef.SessionStarted, sessionRef.SessionCompleted ?? sessionRef.TariffEnd);
if (!subscriptionSearchPeriod.HasValue)
{
    continue;
}
```

```csharp
// Only write to storage if the grain is being deactivated due to application request or idle timeout.
// This avoids unnecessary writes during silo shutdowns or failures, which can cause additional delays
// and problems if the shutdown is due to an error.
//
// NOTE: Skipping the save here can only result in outbox messages being sent multiple times, never
// in lost messages. Inheriting grains are expected to save state after adding messages to the outbox.
if (reason.ReasonCode is DeactivationReasonCode.ApplicationRequested or DeactivationReasonCode.ActivationIdle)
{
    await WriteStateAsync(State, nameof(OnDeactivateAsync), forceWrite: false, cancellationToken);
}
```

```csharp
// We only pass the received session reference to AddSessionUpdatesToOutbox: it is the only session
// that can have changed due to this event, so fanning out updates for the rest would generate
// redundant outbox traffic and observer notifications.
state = AddSessionUpdatesToOutbox(state, [@event.SessionReference], timeProvider, sourceEventInfo: $"{nameof(SessionUpdatedEvent)} with timestamp {@event.Timestamp:O}");
```

```csharp
// OCPI requires a "fallback" Tariff Element with no restrictions to be placed last for each
// dimension; otherwise sessions outside any restricted element have no price component.
// See: https://github.com/ocpi/ocpi/blob/d7d82b6524106e0454101d8cde472cd6f807d9c7/mod_tariffs.asciidoc#131-tariff-object
elements.Add(BuildUnrestrictedFallbackElement(consumptionPriceGroup));
```

```csharp
// Best effort: allow deactivation to continue even if reminder storage/register fails.
// The outbox remains persisted and will be retried by normal traffic reactivating the grain.
LogFailedToRegisterOutboxReminder(ex, this.GetGrainId(), State.Outbox.Length);
```

Parsing comments should show the raw shape and important edge cases:

```csharp
// CPMS session messages arrive on the event hub as JSON shaped like:
// { "sessionId": "abcd-1234", "evseId": "DK*CLE*E0001*1", "started": "2026-05-10T18:34:22.123Z",
//   "completed": null, "energyKwh": 4.20, "subscriptions": [ ... ] }
// `completed` is null while the session is still active; treat that as "ongoing", not invalid.
var session = JsonSerializer.Deserialize<CpmsSessionMessage>(payload, SerializerOptions);
```

```csharp
// WAAS spot-price rows are emitted per quarter-hour and may include partial windows at DST
// transitions (3 quarters on spring-forward, 5 on fall-back). The aggregator requires exactly
// 4 quarters per hour, so windows of any other length are skipped rather than averaged.
if (quartersInHour.Count != 4)
{
    continue;
}
```

Avoid comments that restate the code:

```csharp
// Set the timeout to two seconds.
var timeout = TimeSpan.FromSeconds(2);

// Create a list of subscriptions.
var subscriptions = new List<EvseSubscription>();
```
