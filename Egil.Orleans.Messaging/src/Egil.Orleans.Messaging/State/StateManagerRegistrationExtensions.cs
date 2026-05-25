using Egil.Orleans.Messaging.State;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration helpers for keyed <see cref="IStateManagerFactory{T}"/>
/// services on <see cref="IServiceCollection"/>.
/// </summary>
public static class StateManagerRegistrationExtensions
{
    extension(IServiceCollection services)
    {
    /// <summary>
    /// Registers the default keyed <see cref="IStateManagerFactory{T}"/> for the
    /// given <paramref name="storageName"/>.
    /// </summary>
    public IServiceCollection AddDefaultStateManager(string storageName)
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
    public IServiceCollection AddStateManagerFactory(
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
    public IServiceCollection AddStateManagerFactory<TState, TFactory>(string storageName)
        where TState : class, IEquatable<TState>
        where TFactory : class, IStateManagerFactory<TState>
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateStorageName(storageName);

        return services.AddKeyedSingleton<IStateManagerFactory<TState>, TFactory>(storageName);
    }
    }

    internal static void ValidateStorageName(string storageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageName);
    }

    internal static void ValidateOpenGenericFactoryType(Type openGenericFactoryType)
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
