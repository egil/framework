using System.Diagnostics;
using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;

namespace Egil.Orleans.StateMigration;

internal sealed class MigrationResolver(IServiceProvider serviceProvider) : IMigrationResolver
{
    public TTarget Migrate<TSource, TTarget>(TSource source)
    {
        // Prefer static migration to keep versioning logic colocated with the target type and avoid DI lookup overhead.
        if (StaticMigrator<TSource, TTarget>.IsAvailable)
        {
            using Activity? staticMigrationActivity = StateMigrationTelemetry.StartMigrationActivity(
                typeof(TSource),
                typeof(TTarget),
                StateMigrationTelemetry.StaticMigrationKind);

            try
            {
                if (StaticMigrator<TSource, TTarget>.TryMigrate(source, out TTarget? migrated))
                {
                    StateMigrationTelemetry.RecordSuccessfulMigration(
                        typeof(TSource),
                        typeof(TTarget),
                        StateMigrationTelemetry.StaticMigrationKind);
                    StateMigrationTelemetry.SetActivitySuccess(staticMigrationActivity);
                    return migrated;
                }

                StateMigrationTelemetry.SetActivityFailure(staticMigrationActivity, "No static migration delegate was available.");
            }
            catch (Exception exception)
            {
                StateMigrationTelemetry.SetActivityFailure(staticMigrationActivity, exception);
                throw;
            }
        }

        // External migrators are fallback-only, resolved from DI for cases where migration cannot live on the target type.
        IMigrate<TSource, TTarget>[] migrators = [.. serviceProvider.GetServices<IMigrate<TSource, TTarget>>()];

        return migrators.Length switch
        {
            0 => throw new InvalidOperationException(
                $"No migration path is registered from '{typeof(TSource).FullName}' to '{typeof(TTarget).FullName}'."),
            1 => InvokeExternalMigrator(migrators[0], source),
            _ => throw new InvalidOperationException(
                $"Multiple migrators are registered from '{typeof(TSource).FullName}' to '{typeof(TTarget).FullName}'."),
        };
    }

    private static TTarget InvokeExternalMigrator<TSource, TTarget>(IMigrate<TSource, TTarget> migrator, TSource source)
    {
        using Activity? externalMigrationActivity = StateMigrationTelemetry.StartMigrationActivity(
            typeof(TSource),
            typeof(TTarget),
            StateMigrationTelemetry.ExternalMigrationKind);

        try
        {
            TTarget migrated = migrator.Migrate(source);
            StateMigrationTelemetry.RecordSuccessfulMigration(
                typeof(TSource),
                typeof(TTarget),
                StateMigrationTelemetry.ExternalMigrationKind);
            StateMigrationTelemetry.SetActivitySuccess(externalMigrationActivity);
            return migrated;
        }
        catch (Exception exception)
        {
            StateMigrationTelemetry.SetActivityFailure(externalMigrationActivity, exception);
            throw;
        }
    }

    private static class StaticMigrator<TSource, TTarget>
    {
        // Cache the delegate once per generic pair so hot-path migrations avoid repeated reflection.
        private static readonly Func<TSource, TTarget>? Migrator = CreateMigrator();
        public static bool IsAvailable => Migrator is not null;

        public static bool TryMigrate(TSource source, out TTarget result)
        {
            if (Migrator is null)
            {
                result = default!;
                return false;
            }

            result = Migrator(source);
            return true;
        }

        private static Func<TSource, TTarget>? CreateMigrator()
        {
            if (!typeof(IMigrateFrom<TSource, TTarget>).IsAssignableFrom(typeof(TTarget)))
            {
                return null;
            }

            var fromMethod = typeof(TTarget).GetMethod(
                nameof(IMigrateFrom<TSource, TTarget>.From),
                [typeof(TSource)]);

            if (fromMethod is null || !fromMethod.IsStatic || fromMethod.ReturnType != typeof(TTarget))
            {
                throw new InvalidOperationException(
                    $"Type '{typeof(TTarget).FullName}' implements '{typeof(IMigrateFrom<TSource, TTarget>).FullName}' but no valid static From method was found.");
            }

            // Compile once to strongly-typed delegate so subsequent invocations are allocation-free and fast.
            ParameterExpression sourceParameter = Expression.Parameter(typeof(TSource), "source");
            MethodCallExpression call = Expression.Call(fromMethod, sourceParameter);
            return Expression.Lambda<Func<TSource, TTarget>>(call, sourceParameter).Compile();
        }
    }
}
