using System.Text.Json.Serialization.Metadata;
using Orleans.Journaling.Json;

namespace Egil.Orleans.Messaging.Journaling.Tests;

public static class PrototypeHostingExtensions
{
    public static ISiloBuilder AddMessagingJournalingPrototype(this ISiloBuilder silo)
    {
        silo.UseJsonJournalFormat(options => options.AddTypeInfoResolver(new DefaultJsonTypeInfoResolver()));
        // Register twice so every cluster scenario also exercises registration idempotency.
        return silo.AddMessagingJournaling().AddMessagingJournaling();
    }
}
