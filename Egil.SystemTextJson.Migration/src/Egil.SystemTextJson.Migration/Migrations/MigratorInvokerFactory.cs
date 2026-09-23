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

    public static IMigratorInvoker CreateStaticInvoker(Type sourceType, Type targetType)
    {
        if (ImplementsMigrationContract(targetType, sourceType))
        {
            return (IMigratorInvoker)Activator.CreateInstance(
                typeof(StaticMigratorInvoker<,>).MakeGenericType(sourceType, targetType))!;
        }

        // Older discovery also accepted a derived-target overload when the inherited
        // interface names a base target. Such targets cannot satisfy the direct-call
        // constraint, but a cached typed delegate preserves their existing behavior.
        MethodInfo method = FindInheritedTargetOverload(sourceType, targetType);
        Delegate migrate = method.CreateDelegate(typeof(TryMigrateDelegate<,>).MakeGenericType(sourceType, targetType));
        return (IMigratorInvoker)Activator.CreateInstance(
            typeof(InheritedMigratorInvoker<,>).MakeGenericType(sourceType, targetType),
            migrate)!;
    }

    private static IMigratorInvoker CreateExternalInvokerGeneric<TSource, TTarget>(
        Type migratorType,
        IServiceProvider? serviceProvider)
        => new ExternalMigratorInvoker<TSource, TTarget>(migratorType, serviceProvider);

    private static bool ImplementsMigrationContract(Type targetType, Type sourceType)
        => targetType.GetInterfaces().Any(contract =>
            contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(IMigrateFrom<,>)
            && contract.GenericTypeArguments[0] == sourceType
            && contract.GenericTypeArguments[1] == targetType);

    private static MethodInfo FindInheritedTargetOverload(Type sourceType, Type targetType)
    {
        MethodInfo? method = targetType.GetMethod(
            nameof(IMigrateFrom<,>.TryMigrateFrom),
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            [sourceType, targetType.MakeByRefType()]);

        // The binder also matches wider parameter types, and a ref parameter has the same type as
        // an out parameter, so check the exact shape the previous resolver required.
        if (method is null
            || method.ReturnType != typeof(bool)
            || method.GetParameters() is not [{ ParameterType: var sourceParameter }, { IsOut: true, ParameterType: var targetParameter }]
            || sourceParameter != sourceType
            || targetParameter != targetType.MakeByRefType())
        {
            throw new InvalidOperationException(
                $"The inherited migration contract on '{targetType}' has no matching static '{nameof(IMigrateFrom<,>.TryMigrateFrom)}' overload.");
        }

        return method;
    }
}
