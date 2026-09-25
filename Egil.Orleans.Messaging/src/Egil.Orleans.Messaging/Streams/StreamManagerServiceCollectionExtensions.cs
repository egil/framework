using Egil.Orleans.Messaging.Streams;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Silo-wide <see cref="StreamManager"/> defaults on <see cref="IServiceCollection"/>.
/// </summary>
public static class StreamManagerServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Sets the defaults every <see cref="StreamManager"/> subscription in the
        /// silo starts from.
        /// </summary>
        /// <remarks>
        /// Each call adds a configuration step, applied in registration order, as
        /// <c>services.Configure&lt;StreamSubscriptionOptions&gt;(...)</c> does. A
        /// subscription's own <c>configure</c> callback runs after all of them.
        /// </remarks>
        /// <param name="configure">Configures the default <see cref="StreamSubscriptionOptions"/>.</param>
        /// <returns>The service collection, for chaining.</returns>
        public IServiceCollection ConfigureStreamManager(Action<StreamSubscriptionOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.AddOptions<StreamSubscriptionOptions>().Configure(configure);
            return services;
        }

        /// <summary>
        /// Sets the defaults every <see cref="StreamManager"/> subscription in the
        /// silo starts from, with access to the silo's services.
        /// </summary>
        /// <remarks>
        /// Use this overload to share a registered service with every subscription,
        /// for example a keyed domain clock:
        /// <code>
        /// services.ConfigureStreamManager((options, sp) =>
        ///     options.TimeProvider = sp.GetRequiredKeyedService&lt;TimeProvider&gt;("pricing"));
        /// </code>
        /// </remarks>
        /// <param name="configure">
        /// Configures the default <see cref="StreamSubscriptionOptions"/> with the
        /// silo's <see cref="IServiceProvider"/>.
        /// </param>
        /// <returns>The service collection, for chaining.</returns>
        public IServiceCollection ConfigureStreamManager(Action<StreamSubscriptionOptions, IServiceProvider> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.AddOptions<StreamSubscriptionOptions>().Configure(configure);
            return services;
        }
    }
}
