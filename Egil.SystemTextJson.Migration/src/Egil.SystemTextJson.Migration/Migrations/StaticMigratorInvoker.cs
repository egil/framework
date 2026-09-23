namespace Egil.SystemTextJson.Migration.Migrations;

internal sealed class StaticMigratorInvoker<TSource, TTarget> : MigratorInvoker<TSource, TTarget>
    where TTarget : IMigrateFrom<TSource, TTarget>
{
    public override bool TryMigrate(TSource source, out TTarget migrated)
        => TTarget.TryMigrateFrom(source, out migrated);
}

internal sealed class InheritedMigratorInvoker<TSource, TTarget>(TryMigrateDelegate<TSource, TTarget> migrate)
    : MigratorInvoker<TSource, TTarget>
{
    public override bool TryMigrate(TSource source, out TTarget migrated) => migrate(source, out migrated);
}
