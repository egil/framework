using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Journaling;
using Orleans.Journaling.Json;

namespace Egil.Orleans.Messaging.Journaling;

/// <summary>Registers the journaled messaging components in an Orleans silo.</summary>
public static class MessagingJournalingExtensions
{
    /// <summary>
    /// Registers named durable outboxes and message trackers using the JSON journal format.
    /// Configure an Orleans journal storage provider separately. Component names must be unique within a grain.
    /// </summary>
    /// <param name="silo">The silo builder.</param>
    /// <returns>The supplied builder.</returns>
    public static ISiloBuilder AddMessagingJournaling(this ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.AddJournalStorage();
        silo.Services.AddOptions<JournaledStateManagerOptions>()
            .Validate(options => options.JournalFormatKey == JsonJournalExtensions.JournalFormatKey,
                "Messaging journaling requires UseJsonJournalFormat().")
            .ValidateOnStart();
        silo.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        silo.Services.TryAddSingleton(typeof(JournalCodec<>));
        silo.Services.TryAddKeyedScoped(typeof(IDurableOutbox<>), KeyedService.AnyKey, typeof(DurableOutbox<>));
        silo.Services.TryAddKeyedScoped<IDurableMessageTracker, DurableMessageTracker>(KeyedService.AnyKey);
        return silo;
    }
}
