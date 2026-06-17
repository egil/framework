using System.Text.Json.Serialization;
using Egil.Orleans.Messaging.Streams;
using Orleans.Streams;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration helpers for provider-specific stream sequence token JSON
/// converters.
/// </summary>
public static class StreamSequenceTokenJsonConverterServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers a JSON converter for a concrete stream sequence token type.
        /// </summary>
        /// <remarks>
        /// The converter is registered in the process-wide registry used by the
        /// built-in System.Text.Json converters, since grain-state converters
        /// are not resolved from the application service provider.
        /// </remarks>
        public IServiceCollection AddStreamSequenceTokenJsonConverter<TToken, TConverter>(
            string typeDescriptor)
            where TToken : StreamSequenceToken
            where TConverter : JsonConverter<TToken>, new()
        {
            ArgumentNullException.ThrowIfNull(services);

            StreamSequenceTokenJsonConverters.Register<TToken, TConverter>(typeDescriptor);

            return services;
        }

        /// <summary>
        /// Registers a JSON converter instance for a concrete stream sequence
        /// token type.
        /// </summary>
        /// <remarks>
        /// The converter is registered in the process-wide registry used by the
        /// built-in System.Text.Json converters, since grain-state converters
        /// are not resolved from the application service provider.
        /// </remarks>
        public IServiceCollection AddStreamSequenceTokenJsonConverter<TToken>(
            string typeDescriptor,
            JsonConverter<TToken> converter)
            where TToken : StreamSequenceToken
        {
            ArgumentNullException.ThrowIfNull(services);

            StreamSequenceTokenJsonConverters.Register(typeDescriptor, converter);

            return services;
        }
    }
}
