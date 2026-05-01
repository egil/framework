# Recipes

Scenario-driven guides for common Egil.Orleans.Testing tasks. Each recipe shows a problem, a working code example, and brief notes.

All code samples are extracted from the [samples project](../../samples/Egil.Orleans.Testing.Samples/) — they compile and run as part of CI.

## Getting started

- [Registering the collector](getting-started.md#registering-the-collector)
- [Inline setup in a test class](getting-started.md#inline-setup-in-a-test-class)
- [Using a shared assembly fixture](getting-started.md#using-a-shared-assembly-fixture)

## Assertion patterns

- [Waiting for any grain activity](assertion-patterns.md#waiting-for-any-grain-activity)
- [Grain-scoped assertions](assertion-patterns.md#grain-scoped-assertions)
- [Returning values from assertions](assertion-patterns.md#returning-values-from-assertions)
- [Configuring the timeout](assertion-patterns.md#configuring-the-timeout)

## Advanced assertions

- [Waiting for a specific storage operation](advanced-assertions.md#waiting-for-a-specific-storage-operation)
- [Waiting for a specific grain call](advanced-assertions.md#waiting-for-a-specific-grain-call)
- [When to prefer advanced over standard assertions](advanced-assertions.md#when-to-prefer-advanced-over-standard-assertions)

## Timers and reminders

- [Grain timers — supported via storage observation](timers-and-reminders.md#grain-timers)
- [Orleans reminders — guidance and TimeProvider support](timers-and-reminders.md#orleans-reminders)

## Streams

- [Explicit stream subscriptions](streams.md#explicit-stream-subscriptions)
- [Implicit stream subscriptions](streams.md#implicit-stream-subscriptions)
- [Fixture setup for streams](streams.md#fixture-setup)
