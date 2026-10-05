# Event Hubs token compatibility

The stable core and Event Hubs extension retain their Orleans 10.3.1 minimum.
Only Event Hubs compilation uses the two 10.4 streaming/provider assemblies,
acquired through `PackageDownload`; restore, runtime and pack graphs retain 10.3.1.

The enriched token overrides 10.4's protected compatibility-domain getter with
`typeof(EventHubSequenceToken)`, matching standard provider tokens in both comparison
directions. On 10.3.1, the CLR introduces an unused virtual slot because the base
getter does not exist. No explicit override mapping or missing base call is emitted.
This is established by actual packed-binary execution, not nuspec metadata alone.
Aliases and JSON discriminator values are unchanged. Provider plus full `StreamId`
remain the checkpoint identity. Generic-token public equality remains separate from
the provider's historical exact V1 checkpoint normalization at recovery boundaries.

```powershell
pwsh -File Egil.Orleans.Messaging/scripts/test-eventhubs-compatibility.ps1
```

The script packs once, checks the dependency minimum, uses a fresh package cache and
runs existing Event Hubs tests as external consumers on 10.3.1 and 10.4.0. Additional
10.4 cases cover the inherited per-event factory, real pooled provider cache recovery
for standard/enriched/historical exact V1 tokens, genuine eviction misses and generic
token rejection. Existing cases cover persisted JSON fixtures, Orleans serialization,
custom containers, null per-event tokens, metadata and outbox request context. SHA-256
verification checks both the unchanged package and each consumer's copied assembly against the packed DLL. Each run gets a unique feed and cache even when the output parent is reused.

Before the override, both symmetric equality cases failed on the packed 10.4
consumer; after the override, both version consumers passed. Broker connectivity and
a live pulling-agent subscription handshake remain coordinated validation work for
#285; this in-process suite does not claim either. No claim covers future versions or
NativeAOT.

Issue: https://github.com/egil/framework/issues/282
Upstream: https://github.com/dotnet/orleans/pull/11155

