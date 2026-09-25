using Egil.Orleans.Messaging.Streams;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Hosting;

/// <summary>
/// Silo-wide <see cref="StreamManager"/> defaults on <see cref="ISiloBuilder"/>.
/// </summary>
public static class StreamManagerSiloBuilderExtensions
{
    extension(ISiloBuilder builder)
    {
        /// <inheritdoc cref="StreamManagerServiceCollectionExtensions.ConfigureStreamManager(IServiceCollection, Action{StreamSubscriptionOptions})"/>
        public ISiloBuilder ConfigureStreamManager(Action<StreamSubscriptionOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configure);

            builder.ConfigureServices(services => services.ConfigureStreamManager(configure));
            return builder;
        }

        /// <inheritdoc cref="StreamManagerServiceCollectionExtensions.ConfigureStreamManager(IServiceCollection, Action{StreamSubscriptionOptions, IServiceProvider})"/>
        public ISiloBuilder ConfigureStreamManager(Action<StreamSubscriptionOptions, IServiceProvider> configure)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configure);

            builder.ConfigureServices(services => services.ConfigureStreamManager(configure));
            return builder;
        }
    }
}
