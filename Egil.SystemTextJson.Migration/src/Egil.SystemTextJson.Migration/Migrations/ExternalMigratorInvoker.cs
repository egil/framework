using System.Reflection;

namespace Egil.SystemTextJson.Migration.Migrations;

internal sealed class ExternalMigratorInvoker<TSource, TTarget> : IMigratorInvoker
{
    private readonly Type migratorType;
    private readonly IServiceProvider? serviceProvider;
    private readonly Lazy<IMigrate<TSource, TTarget>> fallbackMigrator;

    public ExternalMigratorInvoker(Type migratorType, IServiceProvider? serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(migratorType);

        if (!typeof(IMigrate<TSource, TTarget>).IsAssignableFrom(migratorType))
        {
            throw new ArgumentException(
                $"Type '{migratorType.FullName}' does not implement {typeof(IMigrate<TSource, TTarget>).FullName}.",
                nameof(migratorType));
        }

        this.migratorType = migratorType;
        this.serviceProvider = serviceProvider;
        fallbackMigrator = new Lazy<IMigrate<TSource, TTarget>>(CreateFallbackMigrator, true);
    }

    public bool TryMigrate(object? source, out object? migrated)
    {
        if (source is TSource typed && ResolveMigrator().TryMigrateFrom(typed, out TTarget? result))
        {
            migrated = result;
            return true;
        }

        migrated = null;
        return false;
    }

    private IMigrate<TSource, TTarget> ResolveMigrator()
    {
        if (serviceProvider?.GetService(migratorType) is { } service)
        {
            if (service is IMigrate<TSource, TTarget> typedMigrator)
            {
                return typedMigrator;
            }

            throw new InvalidOperationException(
                $"Service provider returned instance of type '{service.GetType().FullName}' for migrator '{migratorType.FullName}', but the instance is not assignable to the migrator type.");
        }

        return fallbackMigrator.Value;
    }

    private IMigrate<TSource, TTarget> CreateFallbackMigrator()
    {
        try
        {
            return Activator.CreateInstance(migratorType) as IMigrate<TSource, TTarget>
                ?? throw new InvalidOperationException($"Migrator type '{migratorType.FullName}' could not be created.");
        }
        catch (Exception exception) when (exception is MissingMethodException or MemberAccessException or TargetInvocationException)
        {
            throw new InvalidOperationException(
                $"Migrator type '{migratorType.FullName}' could not be created. Register it in the configured IServiceProvider or add an accessible parameterless constructor.",
                exception);
        }
    }
}
