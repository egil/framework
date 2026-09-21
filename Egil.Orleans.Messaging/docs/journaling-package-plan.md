# Journaling companion package plan

Status: implemented as a separate preview companion. See the [package README](../src/Egil.Orleans.Messaging.Journaling/README.md) for the shipped API, compatibility policy, verification, and release procedure. This document records the agreed design.

Ship the integration as **Egil.Orleans.Messaging.Journaling**, a separate,
opt-in NuGet package while Orleans Journaling remains experimental. Follow
Orleans' convention of public durable interfaces and internal implementations.

## Package boundary

The dependency direction is:

```text
Egil.Orleans.Messaging.Journaling
  +-- Egil.Orleans.Messaging
  +-- Microsoft.Orleans.Journaling (preview)
```

Core OM continues to own
`MessageTracker`, `Outbox<T>`, envelopes, sequence tokens, and `OutboxProcessor<T>`.
It gains no package reference to Orleans Journaling and no journaling-specific
base class requirement for its consumers.

The companion contains:

| Surface | Types and responsibility |
| --- | --- |
| Public | `IDurableMessageTracker` and `IDurableOutbox<T>`: grain-facing reads, mutations, and `AsImmutable()`. |
| Public | Hosting/registration extensions for named components and arbitrary outbox payload types. `AddMessagingJournaling` registers all named components. |
| Internal | `DurableMessageTracker` and `DurableOutbox<T>`: Orleans lifecycle participation, mutation tracking, diffs, snapshots, and replay. |
| Internal | Operation records, codec integration, and any shared lifecycle helper worth retaining. Their persisted representations need a documented compatibility policy even though their CLR types are internal. |

The preview Orleans lifecycle interfaces stay implementation details of our
components. Orleans supplies the journal manager and storage-provider contracts;
OM does not introduce another manager or storage provider for this integration.
The existing OM `IStateManager<T>` does not participate in the shared journal.

## Preserve the agreed behavior

- Grain code works directly with the durable interfaces. No public `Current` or
  `Committed` properties.
- `AsImmutable()` returns the internally held immutable value without copying.
  Later mutations replace that reference, leaving previously returned values
  unchanged. The existing processor contract stays `Outbox<T>`.
- The outbox keeps its original/current values privately and derives net journal
  operations when Orleans gathers a write. Messages added and removed before
  that write contribute metadata, but no payload, to the resulting delta.
- The tracker currently records individual operations, including original receive
  timestamps. Applying the outbox's diff strategy to it is a separate decision.
- Saving and posting remain independent choices made by the grain.
- Business state, tracker, and outbox use the same Orleans journal manager for a
  shared write. Full snapshots, incremental replay, and failure recovery retain
  message identities, revisions, and sequence history.

## Repository layout

Add these projects to the existing OM solution:

```text
Egil.Orleans.Messaging/
  src/Egil.Orleans.Messaging.Journaling/
    Egil.Orleans.Messaging.Journaling.csproj
    README.md
  test/Egil.Orleans.Messaging.Journaling.Tests/
    Egil.Orleans.Messaging.Journaling.Tests.csproj
```

Move journaling scenarios into the dedicated test project and remove the core
test project's reference to the prototype. Sample grains, sample messages,
recording transports, and volatile/fault-injecting storage belong in samples or
tests, not the shipped assembly.

The prototype currently uses internal core reconstruction constructors and
tracker entry views. Replace its friend-assembly entry with the companion
assembly name, keeping that access narrow rather than publishing storage-only
constructors. Verify the companion against its supported core package version.

## Preview and release policy

Keep the new package explicitly prerelease independently of the core package's
stability. Give it package-specific version configuration and document its
supported core OM and Orleans versions. Do not silently upgrade the Orleans
journaling API while extracting the prototype: its verified baseline is
`Microsoft.Orleans.Journaling` 10.3.1-alpha.1. An upstream API upgrade needs its own
implementation and verification step.

The current OM workflow packs every `src/*/*.csproj` and publishes every resulting
`.nupkg`; versioning and release tagging are currently shared at the OM root.
Before adding a packable companion, make package/version selection explicit so:

- Core releases can proceed independently of the journaling preview.
- A stable core release cannot remove the companion's prerelease designation.
- Publishing the journaling package is an explicit release choice.
- Tags and release notes identify the versions actually being published.

Reuse the existing build, test, and package-validation infrastructure. A separate
NuGet package does not require a separate repository or solution. The final
workflow shape is part of the packaging implementation.

## Implementation slices

1. **Extract the package and its tests.** Create the companion project, move the
   durable types, internalize implementations and operation types, establish
   friend-assembly access, and move sample/test dependencies out of core tests.
   Preserve behavior on the currently pinned Orleans version.
2. **Finish the consumer API and registration.** Replace the sample's `OrderEvent`
   and fixed-name registrations with named registration for arbitrary payload
   types. Add XML documentation required by the shipped-project build rules and
   a package README. Keep `AsImmutable()` and existing processor integration.
3. **Define persisted-format support.** Document operation/snapshot shapes and
   compatibility across preview upgrades. Complete the supported codec and
   serializer-metadata registration path, including restart/replay evidence for
   the formats the package advertises. Do not advertise AOT or migration support
   without implementing and verifying it.
4. **Integrate packaging and release selection.** Add prerelease version policy,
   explicit publishing selection, package metadata, and release notes. Inspect
   the generated packages and exercise an external consumer using packed NuGets.

## Completion evidence

Retain the prototype's real-Orleans behavior scenarios: shared commit and recovery,
net diffs, zero-copy immutable access, posting without saving, scoped tracker
replay, compaction, interleaved mutations, and write failures. Add registration
coverage for multiple component names and payload types and a restart test using
a process-durable journal provider before claiming process-restart durability.

Run the OM solution's Release build/tests and pack validation. Inspect the generated
core `.nuspec` and an isolated core-only consumer restore to confirm that installing
core OM does not pull in Orleans Journaling. Restore and run a separate consumer
against the packed companion to verify dependency versions, registration, normal
mutations, immutable processor access, and shared writes without project references.

Sample grains and storage fixtures now live in the dedicated test project; the companion assembly contains only library types.
