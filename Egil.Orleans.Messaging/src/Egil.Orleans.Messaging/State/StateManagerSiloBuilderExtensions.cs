using Microsoft.Extensions.DependencyInjection;
using Egil.Orleans.Messaging.State;

namespace Orleans.Hosting;

/// <summary>
/// Registration helpers for keyed <see cref="IStateManagerFactory"/>
/// services on <see cref="ISiloBuilder"/>.
/// </summary>
public static class StateManagerSiloBuilderExtensions
{
    extension(ISiloBuilder builder)
    {
        /// <summary>
        /// Registers the default keyed state manager factory on the silo builder.
        /// </summary>
        public ISiloBuilder AddDefaultStateManager(string storageName)
        {
            ArgumentNullException.ThrowIfNull(builder);
            StateManagerRegistrationExtensions.ValidateStorageName(storageName);

            builder.ConfigureServices(services => services.AddDefaultStateManager(storageName));
            return builder;
        }

        /// <summary>
        /// Registers a custom keyed state manager factory on the silo builder.
        /// </summary>
        public ISiloBuilder AddStateManagerFactory(
            string storageName,
            Type factoryType)
        {
            ArgumentNullException.ThrowIfNull(builder);
            StateManagerRegistrationExtensions.ValidateStorageName(storageName);
            StateManagerRegistrationExtensions.ValidateFactoryType(factoryType);

            builder.ConfigureServices(services =>
                services.AddStateManagerFactory(storageName, factoryType));
            return builder;
        }

        /// <summary>
        /// Registers a custom keyed state manager factory on the silo builder.
        /// </summary>
        public ISiloBuilder AddStateManagerFactory<TFactory>(string storageName)
            where TFactory : class, IStateManagerFactory
        {
            ArgumentNullException.ThrowIfNull(builder);
            StateManagerRegistrationExtensions.ValidateStorageName(storageName);

            builder.ConfigureServices(services =>
                services.AddStateManagerFactory<TFactory>(storageName));
            return builder;
        }
    }
}
