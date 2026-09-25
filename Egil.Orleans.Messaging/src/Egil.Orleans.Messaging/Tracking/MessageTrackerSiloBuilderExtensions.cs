using Egil.Orleans.Messaging.Tracking;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Hosting;

/// <summary>
/// Silo-wide <see cref="MessageTracker"/> settings on <see cref="ISiloBuilder"/>.
/// </summary>
public static class MessageTrackerSiloBuilderExtensions
{
    extension(ISiloBuilder builder)
    {
        /// <inheritdoc cref="MessageTrackerServiceCollectionExtensions.ConfigureMessageTracker(IServiceCollection, Action{MessageTrackerOptions})"/>
        public ISiloBuilder ConfigureMessageTracker(Action<MessageTrackerOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configure);

            builder.ConfigureServices(services => services.ConfigureMessageTracker(configure));
            return builder;
        }

        /// <inheritdoc cref="MessageTrackerServiceCollectionExtensions.ConfigureMessageTracker(IServiceCollection, Action{MessageTrackerOptions, IServiceProvider})"/>
        public ISiloBuilder ConfigureMessageTracker(Action<MessageTrackerOptions, IServiceProvider> configure)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configure);

            builder.ConfigureServices(services => services.ConfigureMessageTracker(configure));
            return builder;
        }
    }
}
