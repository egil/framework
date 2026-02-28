using System.Reflection;

namespace Egil.SystemTextJson.Migration.Migrations;

internal static class MigratorInvokerFactory
{
    public static IMigratorInvoker CreateExternalInvoker(
        Type sourceType,
        Type targetType,
        Type migratorType,
        IServiceProvider? serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(migratorType);

        MethodInfo method = typeof(MigratorInvokerFactory)
            .GetMethod(nameof(CreateExternalInvokerGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(sourceType, targetType);

        return (IMigratorInvoker)method.Invoke(null, [migratorType, serviceProvider])!;
    }

    public static IMigratorInvoker CreateStaticInvoker(Type sourceType, Type targetType, MethodInfo method)
    {
        MethodInfo factoryMethod = typeof(MigratorInvokerFactory)
            .GetMethod(nameof(CreateStaticInvokerGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(sourceType, targetType);

        return (IMigratorInvoker)factoryMethod.Invoke(null, [method])!;
    }

    private static IMigratorInvoker CreateExternalInvokerGeneric<TSource, TTarget>(
        Type migratorType,
        IServiceProvider? serviceProvider)
        => new ExternalMigratorInvoker<TSource, TTarget>(migratorType, serviceProvider);

    private static IMigratorInvoker CreateStaticInvokerGeneric<TSource, TTarget>(MethodInfo method)
    {
        var migrator = (TryMigrateDelegate<TSource, TTarget>)method.CreateDelegate(typeof(TryMigrateDelegate<TSource, TTarget>));
        return new StaticMigratorInvoker<TSource, TTarget>(migrator);
    }
}
