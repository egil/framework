# Event Hubs token compatibility on Orleans 10.4

All OM packages require Orleans 10.4.0 or later. The preview Journaling integration
uses Microsoft.Orleans.Journaling 10.4.0-alpha.1. Consumers must upgrade their Orleans
packages together; no mixed 10.3/10.4 runtime or compile-reference strategy is supported.
Shared dependency pins and coordinated package verification are owned by #285.

The enriched token overrides the protected compatibility-domain getter with
`typeof(EventHubSequenceToken)`, matching standard provider tokens in both comparison
directions. Aliases, field IDs and JSON discriminator values are unchanged. Provider
plus full `StreamId` remain the checkpoint identity. Generic-token public equality
remains separate from the provider's historical exact V1 checkpoint normalization at
recovery boundaries; upgrading the runtime does not discard existing checkpoint data.

Factory and cache recovery tests now run in the normal Event Hubs test project:

```powershell
dotnet test Egil.Orleans.Messaging/Egil.Orleans.Messaging.slnx -c Release
```

Coverage includes symmetric equality/order/hash, sequence-before-index ordering,
persisted standard/enriched provider/full-stream checkpoint lookup, Orleans and JSON
serialization, default/custom/null-token containers, per-event metadata, inherited
factory cloning, historical exact V1 recovery through the real pooled provider cache,
and genuine eviction misses. The cache fixtures create separate broker events through
the enriched adapter.

Before the override, standard/enriched symmetric equality failed on Orleans 10.4;
the override restores both comparison directions. Actual StreamManager resume and
the live pulling-agent subscription handshake remain coordinated validation work for
#285. In-process cache tests do not claim broker connectivity or live handshake proof.

Issue: https://github.com/egil/framework/issues/282
Upstream: https://github.com/dotnet/orleans/pull/11155
