# Recipes

Scenario-driven guides for common Egil.Orleans.Testing tasks. Each recipe shows a problem, a working code example, and brief notes.

All code samples are extracted from the [samples project](../../samples/Egil.Orleans.Testing.Samples/) — they compile and run as part of CI.

## Assertion patterns

- [Waiting for any grain activity](assertion-patterns.md#waiting-for-any-grain-activity)
- [Grain-scoped assertions](assertion-patterns.md#grain-scoped-assertions)
- [Returning values from assertions](assertion-patterns.md#returning-values-from-assertions)
- [Configuring the timeout](assertion-patterns.md#configuring-the-timeout)

## Advanced assertions

- [Collecting storage operations](advanced-assertions.md#collecting-storage-operations)
- [Collecting grain calls](advanced-assertions.md#collecting-grain-calls)
- [When to prefer advanced over standard assertions](advanced-assertions.md#when-to-prefer-advanced-over-standard-assertions)

## Timers and reminders

- [Grain timers — supported via storage observation](timers-and-reminders.md#grain-timers)
- [Orleans reminders — guidance and TimeProvider support](timers-and-reminders.md#orleans-reminders)

## Streams

- [Explicit stream subscriptions](streams.md#explicit-stream-subscriptions)
- [Implicit stream subscriptions](streams.md#implicit-stream-subscriptions)
- [Fixture setup for streams](streams.md#fixture-setup)

## Operation feeds

- [When to use operation feeds](operation-feeds.md#when-to-use-operation-feeds)
- [Collecting storage operations](operation-feeds.md#collecting-storage-operations)
- [Collecting grain calls](operation-feeds.md#collecting-grain-calls)
