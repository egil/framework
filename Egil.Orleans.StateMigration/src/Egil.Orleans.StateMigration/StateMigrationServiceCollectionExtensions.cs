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

        // Validate on startup because duplicate migrators are an application wiring error, not a runtime fallback.
        ValidateDuplicateExternalMigrators(services);
        services.TryAddSingleton<IMigrationResolver, MigrationResolver>();
        return services;
    }

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

    private static string GetReadableTypePair(Type type)
    {
        Type[] genericArguments = type.GetGenericArguments();
        return $"({genericArguments[0].FullName} -> {genericArguments[1].FullName})";
    }
}
