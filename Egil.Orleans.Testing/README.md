# Egil.Orleans.Testing

[![NuGet](https://img.shields.io/nuget/v/Egil.Orleans.Testing.svg)](https://www.nuget.org/packages/Egil.Orleans.Testing)

Deterministic async assertion helpers for [Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans/) test suites.

## Overview

`Egil.Orleans.Testing` provides a `GrainActivityCollector` that monitors grain calls and storage operations during integration tests. Instead of arbitrary `Task.Delay` waits, your assertions are retried automatically each time the collector detects grain activity — making tests both fast and reliable.

The library is **test-framework-agnostic**: it works with xUnit, NUnit, MSTest, or any other framework.

## Getting started

### 1. Install the package

```shell
dotnet add package Egil.Orleans.Testing
```

### 2. Create a collector and register it with your test silo

```csharp
var collector = new GrainActivityCollector();

siloBuilder.AddGrainActivityCollector(collector)
    .CollectStorageActivityFromDefault();
```

### 3. Write deterministic assertions

```csharp
var grain = cluster.GrainFactory.GetGrain<IMyGrain>("key");
await grain.DoSomething();

await collector.WaitForAssertionAsync(grain, async (g) =>
{
    var state = await g.GetState();
    Assert.Equal("expected", state);
});
```

## Features

- **Standard assertions** — `WaitForAssertionAsync` retries on any detected grain activity (calls or storage operations). Ideal for asserting observable grain behavior.
- **Grain-scoped assertions** — filter activity to a specific grain so unrelated activity does not trigger retries.
- **Advanced assertions** — `WaitForStorageOperationAsync` and `WaitForGrainCallAsync` wait for specific events matching a predicate. Use sparingly — these couple tests to implementation details.
- **Configurable timeout** — defaults to 5 seconds, overridable per call or via the `WAIT_FOR_ASSERTION_TIMEOUT_SECONDS` environment variable. Timeout is automatically bypassed when a debugger is attached.

## License

[MIT](../LICENSE)
