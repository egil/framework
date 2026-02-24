using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Egil.Orleans.StateMigration;

/// <summary>
/// Dependency-injection registration helpers for state migration services.
/// </summary>
/// <remarks>
/// Registration validates external migrator uniqueness per source/target pair to fail fast at startup
/// instead of surfacing ambiguity at runtime during state deserialization.
/// </remarks>
public static class StateMigrationServiceCollectionExtensions
{
    /// <summary>
    /// Registers an external migrator as a singleton.
    /// </summary>
    /// <typeparam name="TSource">The source state type.</typeparam>
    /// <typeparam name="TTarget">The target state type.</typeparam>
    /// <typeparam name="TMigrator">The migrator implementation type.</typeparam>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <example>
    /// <code><![CDATA[
    /// services
    ///     .AddStateMigrator<ProfileV1, ProfileV2, ProfileV1ToV2Migrator>()
    ///     .AddStateMigration();
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddStateMigrator<TSource, TTarget, TMigrator>(this IServiceCollection services)
        where TMigrator : class, IMigrate<TSource, TTarget>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IMigrate<TSource, TTarget>, TMigrator>();
        return services;
    }

    /// <summary>
    /// Registers state migration services required by this library.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// More than one external <see cref="IMigrate{TSource, TTarget}"/> is registered for the same
    /// source/target pair.
    /// </exception>
    /// <example>
    /// <code><![CDATA[
    /// services.AddSingleton<IMigrate<ProfileV1, ProfileV2>, ProfileV1ToV2Migrator>();
    /// services.AddStateMigration();
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddStateMigration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        ValidateMigratorRegistrations(services);
        Type[] mappedTypes = GetMigratorMappedTypes(services);
        StateTypeIdentity.RegisterRange(mappedTypes);

        // Validate on startup because duplicate migrators are an application wiring error, not a runtime fallback.
        ValidateDuplicateExternalMigrators(services);
        services.TryAddSingleton<IMigrationResolver, MigrationResolver>();
        return services;
    }

    private static void ValidateMigratorRegistrations(IServiceCollection services)
    {
        foreach (ServiceDescriptor descriptor in services.Where(descriptor => IsExternalMigratorService(descriptor.ServiceType)))
        {
            Type serviceType = descriptor.ServiceType;
            if (serviceType.ContainsGenericParameters)
            {
                throw new InvalidOperationException(
                    $"External migrator registration '{serviceType}' must use closed generic source and target types.");
            }

            Type[] genericArguments = serviceType.GetGenericArguments();
            Type sourceType = genericArguments[0];
            Type targetType = genericArguments[1];
            ValidateResolvableTypeMapping(sourceType);
            ValidateResolvableTypeMapping(targetType);
        }
    }

    private static Type[] GetMigratorMappedTypes(IServiceCollection services)
        => services
            .Where(descriptor => IsExternalMigratorService(descriptor.ServiceType))
            .SelectMany(descriptor => descriptor.ServiceType.GetGenericArguments())
            .Distinct()
            .ToArray();

    private static void ValidateDuplicateExternalMigrators(IServiceCollection services)
    {
        var duplicateMigrators = services
            .Where(descriptor => IsExternalMigratorService(descriptor.ServiceType))
            .GroupBy(descriptor => descriptor.ServiceType)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateMigrators.Length == 0)
        {
            return;
        }

        string duplicatePairs = string.Join(", ", duplicateMigrators.Select(GetReadableTypePair));

        throw new InvalidOperationException(
            $"Duplicate external migrators registered for the same source/target pair: {duplicatePairs}.");
    }

    private static bool IsExternalMigratorService(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IMigrate<,>);

    private static void ValidateResolvableTypeMapping(Type type)
    {
        string identity = StateTypeIdentity.GetIdentity(type);
        if (!StateTypeIdentity.TryResolve(identity, out Type? resolvedType) || resolvedType != type)
        {
            throw new InvalidOperationException(
                $"Unresolved $type mapping for '{type.FullName}'. Identity '{identity}' did not resolve to the same type.");
        }
    }

    private static string GetReadableTypePair(Type type)
    {
        Type[] genericArguments = type.GetGenericArguments();
        return $"({genericArguments[0].FullName} -> {genericArguments[1].FullName})";
    }
}
