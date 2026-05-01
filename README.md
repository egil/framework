# Egil's framework

A collection of libraries!

- **[Strongly Typed Primitives](https://github.com/egil/framework/tree/main/Egil.StronglyTypedPrimitives):** A source generator for creating strongly-typed primitive types that makes it easy to avoid the primitive obsession anti pattern. Add a [StronglyTyped] attribute to a partial record struct to get started.
- **[Extensions to Orleans Storage](https://github.com/egil/framework/tree/main/Egil.Orleans.Storage):** This library provides OpenTelemetry integration for Microsoft Orleans grain storage providers. It enables detailed telemetry collection for grain storage operations with minimal configuration, helping you monitor and analyze storage performance, errors, and usage patterns in your Orleans applications.
- **[Orleans Testing](https://github.com/egil/framework/tree/main/Egil.Orleans.Testing):** Deterministic async assertion helpers for Microsoft Orleans integration tests. Provides a `GrainActivityCollector` that monitors grain calls and storage operations so you can write reliable, signal-driven assertions instead of `Task.Delay` waits.
- **[System.Text.Json Migration](https://github.com/egil/framework/tree/main/Egil.SystemTextJson.Migration):** A migration-focused extension for System.Text.Json that supports explicit migrator registration, optional scoped assembly discovery, and AOT-friendly serialization workflows.
