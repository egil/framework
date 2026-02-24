using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;

namespace Egil.Orleans.StateMigration;

internal sealed class MigrationResolver(IServiceProvider serviceProvider) : IMigrationResolver
{
    public TTarget Migrate<TSource, TTarget>(TSource source)
    {
        if (StaticMigrator<TSource, TTarget>.TryMigrate(source, out TTarget? migrated))
        {
            return migrated;
        }

        IMigrate<TSource, TTarget>[] migrators = [.. serviceProvider.GetServices<IMigrate<TSource, TTarget>>()];

        return migrators.Length switch
        {
            0 => throw new InvalidOperationException(
                $"No migration path is registered from '{typeof(TSource).FullName}' to '{typeof(TTarget).FullName}'."),
            1 => migrators[0].Migrate(source),
            _ => throw new InvalidOperationException(
                $"Multiple migrators are registered from '{typeof(TSource).FullName}' to '{typeof(TTarget).FullName}'."),
        };
    }

    private static class StaticMigrator<TSource, TTarget>
    {
        private static readonly Func<TSource, TTarget>? Migrator = CreateMigrator();

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

            ParameterExpression sourceParameter = Expression.Parameter(typeof(TSource), "source");
            MethodCallExpression call = Expression.Call(fromMethod, sourceParameter);
            return Expression.Lambda<Func<TSource, TTarget>>(call, sourceParameter).Compile();
        }
    }
}
