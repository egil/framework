# Recipes

Scenario-driven guides for common migration tasks. Each recipe shows a problem, a working code example, and brief notes.

All code samples are extracted from the [samples project](../../samples/Egil.SystemTextJson.Migration.Samples/) — they compile and run as part of CI.

## Getting started

- [Setting up migration support](getting-started.md#setting-up-migration-support)
- [Choosing between static and external migration](getting-started.md#choosing-between-static-and-external-migration)

## Migration authoring

- [Writing a static migrator (`IMigrateFrom`)](migration-authoring.md#writing-a-static-migrator)
- [Writing an external migrator (`IMigrate`)](migration-authoring.md#writing-an-external-migrator)
- [Migrating from non-object JSON payloads](migration-authoring.md#migrating-from-non-object-json-payloads)
- [Multi-step migration chain](migration-authoring.md#multi-step-migration-chain)
- [Migrating across many versions](migration-authoring.md#migrating-across-many-versions)

## Registration & discovery

- [Registering a single migrator explicitly](registration-discovery.md#registering-a-single-migrator-explicitly)
- [Assembly scanning](registration-discovery.md#assembly-scanning)
- [Combining static and external migrators](registration-discovery.md#combining-static-and-external-migrators)

## Dependency injection

- [Using DI for external migrators](dependency-injection.md#using-di-for-external-migrators)
- [Scoped migrators](dependency-injection.md#scoped-migrators)

## Type discriminators

- [Customizing discriminator values](type-discriminators.md#customizing-discriminator-values)
- [Customizing the discriminator property name](type-discriminators.md#customizing-the-discriminator-property-name)
- [Deriving discriminators from an existing attribute](type-discriminators.md#deriving-discriminators-from-an-existing-attribute)

## Failure handling

- [Default failure behavior (throw)](failure-handling.md#default-failure-behavior)
- [Falling back to target type deserialization](failure-handling.md#falling-back-to-target-type-deserialization)
- [Returning null on failure](failure-handling.md#returning-null-on-failure)
- [Per-type failure handling override](failure-handling.md#per-type-failure-handling-override)

## Legacy & incremental adoption

- [Adopting the library with existing stored JSON](legacy-adoption.md#adopting-the-library-with-existing-stored-json)
- [Discriminator not in first position](legacy-adoption.md#discriminator-not-in-first-position)

## AOT & source generation

- [Using with source-generated `JsonSerializerContext`](aot-source-gen.md#using-with-source-generated-jsonserializercontext)

## Migration tracking

- [Detecting whether a value was migrated](migration-tracking.md#detecting-whether-a-value-was-migrated)
- [Read-migrate-write-back pattern](migration-tracking.md#read-migrate-write-back-pattern)

## Nested & collection scenarios

- [Nested migratable child inside a current parent](nested-collections.md#nested-migratable-child-inside-a-current-parent)
- [Both parent and child need migration](nested-collections.md#both-parent-and-child-need-migration)
- [Migratable child inside a non-migratable parent](nested-collections.md#migratable-child-inside-a-non-migratable-parent)
- [Migrating items in collections](nested-collections.md#migrating-items-in-collections)

## Non-library STJ features related to migration

- [Using `IJsonOnDeserialized` for post-migration validation](stj-features.md#using-ijsonondeserialized-for-post-migration-validation)
- [Using `IJsonOnSerializing` to prepare data before serialization](stj-features.md#using-ijsononserializing-to-prepare-data-before-serialization)
- [Combining `IJsonOnDeserialized` with `IJsonMigrationTracked`](stj-features.md#combining-ijsonondeserialized-with-ijsonmigrationtracked)

## Real-world integration patterns

- [Database / cache migration on read](integration-patterns.md#database--cache-migration-on-read)
- [Message queue / event store migration](integration-patterns.md#message-queue--event-store-migration)
- [ASP.NET Core integration](integration-patterns.md#aspnet-core-integration)
- [Orleans grain state migration](integration-patterns.md#orleans-grain-state-migration)
- [Using Orleans `[Alias]` as the type discriminator](integration-patterns.md#using-orleans-alias-as-the-type-discriminator)

## Observability / telemetry

- [Enabling the OTel migration counter](observability.md#enabling-the-otel-migration-counter)
- [Monitoring migration volume in production](observability.md#monitoring-migration-volume-in-production)

## Error handling & diagnostics

- [Handling unknown discriminators](error-diagnostics.md#handling-unknown-discriminators)
- [Duplicate discriminator detection](error-diagnostics.md#duplicate-discriminator-detection)
- [`UnmappedMemberHandling.Disallow` compatibility](error-diagnostics.md#unmappedmemberhandlingdisallow-compatibility)
