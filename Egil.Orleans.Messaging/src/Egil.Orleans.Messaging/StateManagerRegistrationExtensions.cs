using Microsoft.Extensions.DependencyInjection;

namespace Egil.Orleans.Messaging;

/// <summary>
/// Registration helpers for keyed <see cref="IStateManagerFactory{T}"/>
/// services on <see cref="IServiceCollection"/>.
/// </summary>
public static class StateManagerRegistrationExtensions
{
    /// <summary>
    /// Registers the default keyed <see cref="IStateManagerFactory{T}"/> for the
    /// given <paramref name="storageName"/>.
    /// </summary>
    public static IServiceCollection AddDefaultStateManager(
        this IServiceCollection services,
        string storageName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateStorageName(storageName);

        return services.AddKeyedSingleton(
            typeof(IStateManagerFactory<>),
            storageName,
            typeof(DefaultStateManagerFactory<>));
    }

    /// <summary>
    /// Registers a custom keyed open-generic <see cref="IStateManagerFactory{T}"/>
    /// implementation for the given <paramref name="storageName"/>.
    /// </summary>
    public static IServiceCollection AddStateManagerFactory(
        this IServiceCollection services,
        string storageName,
        Type openGenericFactoryType)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateStorageName(storageName);
        ValidateOpenGenericFactoryType(openGenericFactoryType);

        return services.AddKeyedSingleton(
            typeof(IStateManagerFactory<>),
            storageName,
            openGenericFactoryType);
    }

    /// <summary>
    /// Registers a custom keyed factory for a specific state type.
    /// </summary>
    public static IServiceCollection AddStateManagerFactory<TState, TFactory>(
        this IServiceCollection services,
        string storageName)
        where TState : class, IEquatable<TState>
        where TFactory : class, IStateManagerFactory<TState>
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateStorageName(storageName);

        return services.AddKeyedSingleton<IStateManagerFactory<TState>, TFactory>(storageName);
    }

    /// <summary>
    /// Registers the default keyed state manager factory on the silo builder.
    /// </summary>
    public static ISiloBuilder AddDefaultStateManager(
        this ISiloBuilder builder,
        string storageName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidateStorageName(storageName);

        builder.ConfigureServices(services => services.AddDefaultStateManager(storageName));
        return builder;
    }

    /// <summary>
    /// Registers a custom keyed open-generic state manager factory
    /// on the silo builder.
    /// </summary>
    public static ISiloBuilder AddStateManagerFactory(
        this ISiloBuilder builder,
        string storageName,
        Type openGenericFactoryType)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidateStorageName(storageName);
        ValidateOpenGenericFactoryType(openGenericFactoryType);

        builder.ConfigureServices(services =>
            services.AddStateManagerFactory(storageName, openGenericFactoryType));
        return builder;
    }

    /// <summary>
    /// Registers a custom keyed state manager factory for a specific state type
    /// on the silo builder.
    /// </summary>
    public static ISiloBuilder AddStateManagerFactory<TState, TFactory>(
        this ISiloBuilder builder,
        string storageName)
        where TState : class, IEquatable<TState>
        where TFactory : class, IStateManagerFactory<TState>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidateStorageName(storageName);

        builder.ConfigureServices(services =>
            services.AddStateManagerFactory<TState, TFactory>(storageName));
        return builder;
    }

    private static void ValidateStorageName(string storageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageName);
    }

    private static void ValidateOpenGenericFactoryType(Type openGenericFactoryType)
    {
        ArgumentNullException.ThrowIfNull(openGenericFactoryType);

        if (!openGenericFactoryType.IsClass || openGenericFactoryType.IsAbstract)
        {
            throw new ArgumentException(
                $"Type '{openGenericFactoryType.FullName}' must be a non-abstract class.",
                nameof(openGenericFactoryType));
        }

        if (!openGenericFactoryType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                $"Type '{openGenericFactoryType.FullName}' must be an open generic type definition.",
                nameof(openGenericFactoryType));
        }

        var implementsFactory = openGenericFactoryType.GetInterfaces().Any(
            iface => iface.IsGenericType
                && iface.GetGenericTypeDefinition() == typeof(IStateManagerFactory<>));

        if (!implementsFactory)
        {
            throw new ArgumentException(
                $"Type '{openGenericFactoryType.FullName}' must implement IStateManagerFactory<>.",
                nameof(openGenericFactoryType));
        }
    }
}
