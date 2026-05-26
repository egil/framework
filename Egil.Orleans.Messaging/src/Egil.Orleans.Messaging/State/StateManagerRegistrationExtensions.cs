using Egil.Orleans.Messaging.State;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration helpers for keyed <see cref="IStateManagerFactory"/>
/// services on <see cref="IServiceCollection"/>.
/// </summary>
public static class StateManagerRegistrationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the default keyed <see cref="IStateManagerFactory"/> for the
        /// given <paramref name="storageName"/>.
        /// </summary>
        public IServiceCollection AddDefaultStateManager(string storageName)
        {
            ArgumentNullException.ThrowIfNull(services);
            ValidateStorageName(storageName);

            return services.AddKeyedSingleton(
                typeof(IStateManagerFactory),
                storageName,
                typeof(DefaultStateManagerFactory));
        }

        /// <summary>
        /// Registers a custom keyed state manager factory
        /// implementation for the given <paramref name="storageName"/>.
        /// </summary>
        public IServiceCollection AddStateManagerFactory(
            string storageName,
            Type factoryType)
        {
            ArgumentNullException.ThrowIfNull(services);
            ValidateStorageName(storageName);
            ValidateFactoryType(factoryType);

            return services.AddKeyedSingleton(
                typeof(IStateManagerFactory),
                storageName,
                factoryType);
        }

        /// <summary>
        /// Registers a custom keyed state manager factory.
        /// </summary>
        public IServiceCollection AddStateManagerFactory<TFactory>(string storageName)
            where TFactory : class, IStateManagerFactory
        {
            ArgumentNullException.ThrowIfNull(services);
            ValidateStorageName(storageName);

            return services.AddKeyedSingleton<IStateManagerFactory, TFactory>(storageName);
        }
    }

    internal static void ValidateStorageName(string storageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageName);
    }

    internal static void ValidateFactoryType(Type factoryType)
    {
        ArgumentNullException.ThrowIfNull(factoryType);

        if (!factoryType.IsClass || factoryType.IsAbstract)
        {
            throw new ArgumentException(
                $"Type '{factoryType.FullName}' must be a non-abstract class.",
                nameof(factoryType));
        }

        if (!typeof(IStateManagerFactory).IsAssignableFrom(factoryType))
        {
            throw new ArgumentException(
                $"Type '{factoryType.FullName}' must implement IStateManagerFactory.",
                nameof(factoryType));
        }
    }
}
