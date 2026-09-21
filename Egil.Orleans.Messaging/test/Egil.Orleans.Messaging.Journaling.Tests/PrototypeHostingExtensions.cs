using System.Text.Json.Serialization.Metadata;
using Orleans.Journaling.Json;

namespace Egil.Orleans.Messaging.Journaling.Tests;

public static class PrototypeHostingExtensions
{
    public static ISiloBuilder AddMessagingJournalingPrototype(this ISiloBuilder silo)
    {
        silo.UseJsonJournalFormat(options => options.AddTypeInfoResolver(new DefaultJsonTypeInfoResolver()));
        return silo.AddMessagingJournaling().AddMessagingJournaling();
    }
}
