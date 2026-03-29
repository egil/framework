namespace Egil.SystemTextJson.Migration.Migrations;

internal sealed class StaticMigratorInvoker<TSource, TTarget>(TryMigrateDelegate<TSource, TTarget> migrator) : IMigratorInvoker
{
    public bool TryMigrate(object? source, out object? migrated)
    {
        if (source is TSource typed && migrator(typed, out TTarget? result))
        {
            migrated = result;
            return true;
        }

        migrated = null;
        return false;
    }
}
