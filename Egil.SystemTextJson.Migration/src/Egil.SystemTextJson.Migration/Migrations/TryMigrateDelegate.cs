namespace Egil.SystemTextJson.Migration.Migrations;

internal delegate bool TryMigrateDelegate<TSource, TTarget>(TSource source, out TTarget result);
