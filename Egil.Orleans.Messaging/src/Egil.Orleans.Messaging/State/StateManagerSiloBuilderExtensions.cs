using Microsoft.Extensions.DependencyInjection;
using Egil.Orleans.Messaging.State;

namespace Orleans.Hosting;

/// <summary>
/// Registration helpers for keyed <see cref="IStateManagerFactory{T}"/>
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
        /// Registers a custom keyed open-generic state manager factory on the silo builder.
        /// </summary>
        public ISiloBuilder AddStateManagerFactory(
            string storageName,
            Type openGenericFactoryType)
        {
            ArgumentNullException.ThrowIfNull(builder);
            StateManagerRegistrationExtensions.ValidateStorageName(storageName);
            StateManagerRegistrationExtensions.ValidateOpenGenericFactoryType(openGenericFactoryType);

            builder.ConfigureServices(services =>
                services.AddStateManagerFactory(storageName, openGenericFactoryType));
            return builder;
        }

        /// <summary>
        /// Registers a custom keyed state manager factory for a specific state type on the silo builder.
        /// </summary>
        public ISiloBuilder AddStateManagerFactory<TState, TFactory>(string storageName)
            where TState : class, IEquatable<TState>
            where TFactory : class, IStateManagerFactory<TState>
        {
            ArgumentNullException.ThrowIfNull(builder);
            StateManagerRegistrationExtensions.ValidateStorageName(storageName);

            builder.ConfigureServices(services =>
                services.AddStateManagerFactory<TState, TFactory>(storageName));
            return builder;
        }
    }
}
