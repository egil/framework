using Egil.Orleans.Messaging.State;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration helpers for keyed <see cref="IStateManagerFactory"/>
/// services on <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// Optional factory callbacks override silo-wide configuration regardless of registration
/// order; callbacks within each layer run in registration order. Named factories use the
/// storage name as the options name, and unkeyed factories use Options.DefaultName.
/// Grain-local callbacks apply last. Each hydrated manager receives a fixed snapshot.
/// </remarks>
public static class StateManagerRegistrationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Configures every named and unkeyed manager before factory and grain overrides.</summary>
        public IServiceCollection ConfigureStateManager(Action<StateManagerOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);
            services.ConfigureAll(configure);
            return services;
        }

        /// <summary>Configures every manager using services from this silo's provider.</summary>
        public IServiceCollection ConfigureStateManager(Action<StateManagerOptions, IServiceProvider> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);
            services.AddOptions();
            services.AddSingleton<IConfigureOptions<StateManagerOptions>>(provider =>
                new ConfigureNamedOptions<StateManagerOptions>(null, options => configure(options, provider)));
            return services;
        }

        /// <summary>Registers an unkeyed custom factory with optional recovery overrides.</summary>
        public IServiceCollection AddStateManagerFactory<TFactory>(Action<StateManagerOptions>? configure = null)
            where TFactory : class, IStateManagerFactory
            => services.AddStateManagerFactory(typeof(TFactory), configure);

        /// <summary>
        /// Allows a grain to inject <see cref="IStateManager{T}"/> directly on a
        /// <c>[PersistentState]</c> constructor parameter, in place of
        /// <see cref="IPersistentState{TState}"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every <see cref="IStateManagerFactory"/> registration helper in this class calls
        /// this, and a grain needs one of those to obtain a manager at all, so it is rarely
        /// called directly. Call it when registering an <see cref="IStateManagerFactory"/>
        /// by hand. Repeat calls are ignored.
        /// </para>
        /// <para>
        /// Grains injecting <see cref="IPersistentState{TState}"/> are unaffected: the
        /// decorated mapper handles the manager shape and hands every other shape to
        /// Orleans' own.
        /// </para>
        /// </remarks>
        public IServiceCollection AddStateManagerFacet()
        {
            ArgumentNullException.ThrowIfNull(services);

            if (services.Any(static descriptor => descriptor.ServiceType == typeof(FacetMarker)))
            {
                return services;
            }

            services.AddOptions<StateManagerOptions>();
            services.AddSingleton(FacetMarker.Instance);

            // Orleans registers its mapper with TryAddSingleton from the SiloBuilder
            // constructor, which runs before any ConfigureServices callback, so the default
            // is always already in place and a TryAdd of our own would silently do nothing.
            // Capture whatever is registered, then take the service type over and delegate
            // back to it for parameter shapes that are not ours.
            var registered = services.LastOrDefault(static descriptor =>
                descriptor.ServiceType == typeof(IAttributeToFactoryMapper<PersistentStateAttribute>));

            services.RemoveAll<IAttributeToFactoryMapper<PersistentStateAttribute>>();

            return services.AddSingleton<IAttributeToFactoryMapper<PersistentStateAttribute>>(
                provider => new StateManagerFacetMapper(ResolveDecoratedMapper(provider, registered)));
        }

        /// <summary>
        /// Registers the default <see cref="IStateManagerFactory"/> for grains that
        /// register a manager without naming a storage name.
        /// </summary>
        /// <remarks>
        /// Use this when one factory serves every managed facet in the silo, so a grain
        /// can call <c>RegisterStateManager(storage)</c> and name its facet only in the
        /// <c>[PersistentState]</c> attribute. Register a keyed factory as well, or
        /// instead, when different storage providers need different failure handling.
        /// </remarks>
        public IServiceCollection AddDefaultStateManager(Action<StateManagerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            ConfigureFactory(services, Microsoft.Extensions.Options.Options.DefaultName, configure);

            return services
                .AddStateManagerFacet()
                .AddSingleton(typeof(IStateManagerFactory), typeof(DefaultStateManagerFactory));
        }

        /// <summary>
        /// Registers a custom <see cref="IStateManagerFactory"/> for grains that register
        /// a manager without naming a storage name.
        /// </summary>
        public IServiceCollection AddStateManagerFactory(Type factoryType, Action<StateManagerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ValidateFactoryType(factoryType);

            ConfigureFactory(services, Microsoft.Extensions.Options.Options.DefaultName, configure);

            return services
                .AddStateManagerFacet()
                .AddSingleton(typeof(IStateManagerFactory), factoryType);
        }

        /// <summary>
        /// Registers the default keyed <see cref="IStateManagerFactory"/> for the
        /// given <paramref name="storageName"/>.
        /// </summary>
        public IServiceCollection AddDefaultStateManager(string storageName, Action<StateManagerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ValidateStorageName(storageName);

            ConfigureFactory(services, storageName, configure);

            return services
                .AddStateManagerFacet()
                .AddKeyedSingleton(typeof(IStateManagerFactory), storageName, typeof(DefaultStateManagerFactory));
        }

        /// <summary>
        /// Registers a custom keyed state manager factory
        /// implementation for the given <paramref name="storageName"/>.
        /// </summary>
        public IServiceCollection AddStateManagerFactory(
            string storageName,
            Type factoryType, Action<StateManagerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ValidateStorageName(storageName);
            ValidateFactoryType(factoryType);

            ConfigureFactory(services, storageName, configure);

            return services
                .AddStateManagerFacet()
                .AddKeyedSingleton(typeof(IStateManagerFactory), storageName, factoryType);
        }

        /// <summary>
        /// Registers a custom keyed state manager factory.
        /// </summary>
        public IServiceCollection AddStateManagerFactory<TFactory>(string storageName, Action<StateManagerOptions>? configure = null)
            where TFactory : class, IStateManagerFactory
        {
            ArgumentNullException.ThrowIfNull(services);
            ValidateStorageName(storageName);

            ConfigureFactory(services, storageName, configure);

            return services
                .AddStateManagerFacet()
                .AddKeyedSingleton<IStateManagerFactory, TFactory>(storageName);
        }
    }

    private static void ConfigureFactory(IServiceCollection services, string name, Action<StateManagerOptions>? configure)
    {
        services.AddOptions<StateManagerOptions>();
        if (configure is not null)
        {
            // Post-configuration makes factory overrides win even if global callbacks
            // are registered later. Names keep unrelated storage factories isolated.
            services.PostConfigure(name, configure);
        }
    }

    private static IAttributeToFactoryMapper<PersistentStateAttribute> ResolveDecoratedMapper(
        IServiceProvider provider,
        ServiceDescriptor? registered)
        => registered switch
        {
            null => new PersistentStateAttributeMapper(),
            { ImplementationInstance: IAttributeToFactoryMapper<PersistentStateAttribute> instance } => instance,
            { ImplementationFactory: { } factory } =>
                (IAttributeToFactoryMapper<PersistentStateAttribute>)factory(provider),
            { ImplementationType: { } type } =>
                (IAttributeToFactoryMapper<PersistentStateAttribute>)ActivatorUtilities.CreateInstance(provider, type),
            _ => new PersistentStateAttributeMapper(),
        };

    // Presence of this registration marks the facet as already enabled, so the helpers
    // that call AddStateManagerFacet on every factory registration decorate only once.
    // Registered as an instance because nothing ever resolves it: a host building its
    // provider with ValidateOnBuild constructs every descriptor it can, and a marker has
    // no reason to be among them.
    private sealed class FacetMarker
    {
        public static FacetMarker Instance { get; } = new();

        private FacetMarker()
        {
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
