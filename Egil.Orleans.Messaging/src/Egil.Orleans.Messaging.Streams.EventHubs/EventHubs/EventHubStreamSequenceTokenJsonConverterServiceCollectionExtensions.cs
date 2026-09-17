using Egil.Orleans.Messaging.Streams.EventHubs;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration helpers for the Event Hubs stream sequence token JSON converters.
/// </summary>
public static class EventHubStreamSequenceTokenJsonConverterServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the JSON converters for the Event Hubs stream sequence token
        /// types.
        /// </summary>
        /// <remarks>
        /// The converters are registered in the process-wide registry used by the
        /// built-in System.Text.Json converters, since grain-state converters are not
        /// resolved from the application service provider. A silo that calls
        /// <c>UseEnrichedDataAdapter()</c> already gets them; this is for processes
        /// that read the same persisted state without configuring an Event Hub stream
        /// provider. Calling both, in either order, is safe.
        /// </remarks>
        public IServiceCollection AddEventHubStreamSequenceTokenJsonConverters()
        {
            ArgumentNullException.ThrowIfNull(services);

            EventHubStreamSequenceTokenJsonConverters.Register();

            return services;
        }
    }
}
