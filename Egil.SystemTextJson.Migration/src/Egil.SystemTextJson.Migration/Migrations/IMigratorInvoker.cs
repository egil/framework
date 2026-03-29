namespace Egil.SystemTextJson.Migration.Migrations;

internal interface IMigratorInvoker
{
    bool TryMigrate(object? source, out object? migrated);
}
