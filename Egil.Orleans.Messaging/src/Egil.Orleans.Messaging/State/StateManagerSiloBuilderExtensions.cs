using Microsoft.Extensions.DependencyInjection;
using Egil.Orleans.Messaging.State;

namespace Orleans.Hosting;

/// <summary>
/// Registration helpers for keyed <see cref="IStateManagerFactory"/>
/// services on <see cref="ISiloBuilder"/>.
/// </summary>
/// <remarks>
/// Optional factory callbacks override silo-wide configuration regardless of registration
/// order; callbacks within each layer run in registration order. Named factories use the
/// storage name as the options name, and unkeyed factories use Options.DefaultName.
/// Grain-local callbacks apply last. Each hydrated manager receives a fixed snapshot.
/// </remarks>
public static class StateManagerSiloBuilderExtensions
{
    extension(ISiloBuilder builder)
    {
        /// <summary>Configures all managers before named factory and grain-local overrides.</summary>
        public ISiloBuilder ConfigureStateManager(Action<StateManagerOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.ConfigureStateManager(configure);
            return builder;
        }

        /// <summary>Configures all managers using services from this silo's provider.</summary>
        public ISiloBuilder ConfigureStateManager(Action<StateManagerOptions, IServiceProvider> configure)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.ConfigureStateManager(configure);
            return builder;
        }

        /// <summary>Registers the unkeyed default factory with optional recovery overrides.</summary>
        public ISiloBuilder AddDefaultStateManager(Action<StateManagerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.AddDefaultStateManager(configure);
            return builder;
        }

        /// <summary>Registers an unkeyed custom factory with optional recovery overrides.</summary>
        public ISiloBuilder AddStateManagerFactory(Type factoryType, Action<StateManagerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.AddStateManagerFactory(factoryType, configure);
            return builder;
        }

        /// <summary>Registers an unkeyed custom factory with optional recovery overrides.</summary>
        public ISiloBuilder AddStateManagerFactory<TFactory>(Action<StateManagerOptions>? configure = null)
            where TFactory : class, IStateManagerFactory
            => builder.AddStateManagerFactory(typeof(TFactory), configure);

        /// <summary>
        /// Registers the default keyed state manager factory on the silo builder.
        /// </summary>
        public ISiloBuilder AddDefaultStateManager(string storageName, Action<StateManagerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            StateManagerRegistrationExtensions.ValidateStorageName(storageName);

            builder.ConfigureServices(services => services.AddDefaultStateManager(storageName, configure));
            return builder;
        }

        /// <summary>
        /// Registers a custom keyed state manager factory on the silo builder.
        /// </summary>
        public ISiloBuilder AddStateManagerFactory(
            string storageName,
            Type factoryType, Action<StateManagerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            StateManagerRegistrationExtensions.ValidateStorageName(storageName);
            StateManagerRegistrationExtensions.ValidateFactoryType(factoryType);

            builder.ConfigureServices(services =>
                services.AddStateManagerFactory(storageName, factoryType, configure));
            return builder;
        }

        /// <summary>
        /// Registers a custom keyed state manager factory on the silo builder.
        /// </summary>
        public ISiloBuilder AddStateManagerFactory<TFactory>(string storageName, Action<StateManagerOptions>? configure = null)
            where TFactory : class, IStateManagerFactory
        {
            ArgumentNullException.ThrowIfNull(builder);
            StateManagerRegistrationExtensions.ValidateStorageName(storageName);

            builder.ConfigureServices(services =>
                services.AddStateManagerFactory<TFactory>(storageName, configure));
            return builder;
        }
    }
}
