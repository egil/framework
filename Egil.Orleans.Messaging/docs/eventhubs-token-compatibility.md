# Event Hubs token compatibility

The core and Event Hubs packages require Orleans 10.4.0. Compile, restore,
runtime and NuGet dependency graphs use that same release.

The enriched token overrides the provider compatibility-domain getter with
`typeof(EventHubSequenceToken)`, matching standard provider tokens in both
comparison directions. Aliases, field IDs and JSON discriminators are unchanged.
Provider plus complete `StreamId` remain the checkpoint identity. Historical exact
V1 checkpoints remain supported at the provider recovery boundary; this preserves
persisted data and does not imply support for an older Orleans runtime.

Run the Event Hubs test suite:

```powershell
cd Egil.Orleans.Messaging
dotnet test --project test/Egil.Orleans.Messaging.Streams.EventHubs.Tests/Egil.Orleans.Messaging.Streams.EventHubs.Tests.csproj --configuration Release
```

Coverage includes symmetric equality/order/hash, restored checkpoint lookup and
StreamManager resume, JSON and Orleans serialization, metadata and per-event
factory behavior, real pooled-cache recovery for standard/enriched/historical
checkpoints, eviction misses and incompatible token domains. Live Event Hubs
broker and pulling-agent handshake evidence still requires infrastructure and is
reported separately.
No NativeAOT or future-version claim is made.

Issue: https://github.com/egil/framework/issues/282
Upstream: https://github.com/dotnet/orleans/pull/11155
