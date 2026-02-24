using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Egil.Orleans.StateMigration;

public static class StateMigrationServiceCollectionExtensions
{
    public static IServiceCollection AddStateMigration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

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
