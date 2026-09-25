using Egil.Orleans.Messaging.Tracking;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Silo-wide <see cref="MessageTracker"/> settings on <see cref="IServiceCollection"/>.
/// </summary>
public static class MessageTrackerServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Sets the <see cref="MessageTrackerOptions"/> every
        /// <see cref="MessageTracker"/> in the silo uses.
        /// </summary>
        /// <remarks>
        /// Each call adds a configuration step, applied in registration order, as
        /// <c>services.Configure&lt;MessageTrackerOptions&gt;(...)</c> does. The silo
        /// applies the result when it starts, before any grain activates, and removes
        /// it when it stops. Trackers given a clock with
        /// <see cref="MessageTracker.RegisterTimeProvider"/> keep using it.
        /// </remarks>
        /// <param name="configure">Configures the <see cref="MessageTrackerOptions"/>.</param>
        /// <returns>The service collection, for chaining.</returns>
        public IServiceCollection ConfigureMessageTracker(Action<MessageTrackerOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.AddOptions<MessageTrackerOptions>().Configure(configure);
            services.AddMessageTrackerInstaller();
            return services;
        }

        /// <summary>
        /// Sets the <see cref="MessageTrackerOptions"/> every
        /// <see cref="MessageTracker"/> in the silo uses, with access to the silo's services.
        /// </summary>
        /// <remarks>
        /// Use this overload to share a registered service, for example a keyed
        /// domain clock:
        /// <code>
        /// services.ConfigureMessageTracker((options, sp) =>
        ///     options.TimeProvider = sp.GetRequiredKeyedService&lt;TimeProvider&gt;("pricing"));
        /// </code>
        /// </remarks>
        /// <param name="configure">
        /// Configures the <see cref="MessageTrackerOptions"/> with the silo's
        /// <see cref="IServiceProvider"/>.
        /// </param>
        /// <returns>The service collection, for chaining.</returns>
        public IServiceCollection ConfigureMessageTracker(Action<MessageTrackerOptions, IServiceProvider> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.AddOptions<MessageTrackerOptions>().Configure(configure);
            services.AddMessageTrackerInstaller();
            return services;
        }

        private void AddMessageTrackerInstaller() =>
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<ILifecycleParticipant<ISiloLifecycle>, MessageTrackerTimeProviderInstaller>());
    }
}
