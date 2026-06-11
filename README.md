# Egil's framework

A collection of libraries!

- **[Strongly Typed Primitives](https://github.com/egil/framework/tree/main/Egil.StronglyTypedPrimitives):** A source generator for creating strongly-typed primitive types that makes it easy to avoid the primitive obsession anti pattern. Add a [StronglyTyped] attribute to a partial record struct to get started.
- **[Orleans Testing](https://github.com/egil/framework/tree/main/Egil.Orleans.Testing):** Deterministic async assertion helpers for Microsoft Orleans integration tests. Provides a `GrainActivityCollector` that monitors grain calls and storage operations so you can write reliable, signal-driven assertions instead of `Task.Delay` waits.
- **[System.Text.Json Migration](https://github.com/egil/framework/tree/main/Egil.SystemTextJson.Migration):** A migration-focused extension for System.Text.Json that supports explicit migrator registration, optional scoped assembly discovery, and AOT-friendly serialization workflows.
