namespace Egil.Orleans.StateMigration;

internal static class StateMigrationInvoker
{
    public static bool TryMigrate(object source, Type sourceType, Type targetType, out object? migrated)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentNullException.ThrowIfNull(targetType);

        if (sourceType == targetType)
        {
            migrated = source;
            return true;
        }

        Type migrationInterface = typeof(IMigrateFrom<,>).MakeGenericType(sourceType, targetType);
        if (!migrationInterface.IsAssignableFrom(targetType))
        {
            migrated = null;
            return false;
        }

        // Use static target-owned migration when available so converter read-path can migrate without DI.
        var fromMethod = targetType.GetMethod(nameof(IMigrateFrom<object, object>.From), [sourceType]);
        if (fromMethod is null || !fromMethod.IsStatic || fromMethod.ReturnType != targetType)
        {
            throw new InvalidOperationException(
                $"Type '{targetType.FullName}' implements '{migrationInterface.FullName}' but no valid static From method was found.");
        }

        migrated = fromMethod.Invoke(null, [source]);
        return migrated is not null;
    }
}
