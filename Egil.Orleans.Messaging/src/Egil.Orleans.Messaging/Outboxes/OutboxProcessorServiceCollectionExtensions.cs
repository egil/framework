using Egil.Orleans.Messaging.Outboxes;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Silo-wide <see cref="OutboxProcessor{TOutbox}"/> defaults on <see cref="IServiceCollection"/>.
/// </summary>
public static class OutboxProcessorServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Enables the automatic outbox deactivation safeguard, including for
        /// processors registered after the grain constructor.
        /// </summary>
        /// <returns>The service collection, for chaining.</returns>
        public IServiceCollection ConfigureOutboxProcessor()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureGrainContextProvider, OutboxGrainContextConfigurator>());
            return services;
        }

        /// <summary>
        /// Enables the deactivation safeguard and sets the defaults every
        /// <see cref="OutboxProcessor{TOutbox}"/> in the silo starts from.
        /// </summary>
        /// <remarks>
        /// Each call adds a configuration step, applied in registration order, as
        /// <c>services.Configure&lt;OutboxProcessorOptions&gt;(...)</c> does. The
        /// processor's own <c>configure</c> callback runs after all of them.
        /// </remarks>
        /// <param name="configure">Configures the default <see cref="OutboxProcessorOptions"/>.</param>
        /// <returns>The service collection, for chaining.</returns>
        public IServiceCollection ConfigureOutboxProcessor(Action<OutboxProcessorOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.ConfigureOutboxProcessor();
            services.AddOptions<OutboxProcessorOptions>().Configure(configure);
            return services;
        }

        /// <summary>
        /// Enables the deactivation safeguard and sets the defaults every
        /// <see cref="OutboxProcessor{TOutbox}"/> in the silo starts from,
        /// with access to the silo's services.
        /// </summary>
        /// <remarks>
        /// Use this overload to share a registered service with every processor,
        /// for example a keyed domain clock:
        /// <code>
        /// services.ConfigureOutboxProcessor((options, sp) =>
        ///     options.TimeProvider = sp.GetRequiredKeyedService&lt;TimeProvider&gt;("pricing"));
        /// </code>
        /// </remarks>
        /// <param name="configure">
        /// Configures the default <see cref="OutboxProcessorOptions"/> with the
        /// silo's <see cref="IServiceProvider"/>.
        /// </param>
        /// <returns>The service collection, for chaining.</returns>
        public IServiceCollection ConfigureOutboxProcessor(Action<OutboxProcessorOptions, IServiceProvider> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.ConfigureOutboxProcessor();
            services.AddOptions<OutboxProcessorOptions>().Configure(configure);
            return services;
        }
    }
}
