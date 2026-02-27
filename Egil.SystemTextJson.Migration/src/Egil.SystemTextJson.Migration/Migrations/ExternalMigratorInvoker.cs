namespace Egil.SystemTextJson.Migration.Migrations;

internal sealed class ExternalMigratorInvoker<TSource, TTarget>(IMigrate<TSource, TTarget> migrator) : IMigratorInvoker
{
    public bool TryMigrate(object? source, out object? migrated)
    {
        if (source is TSource typed && migrator.TryMigrateFrom(typed, out TTarget? result))
        {
            migrated = result;
            return true;
        }

        migrated = null;
        return false;
    }
}
