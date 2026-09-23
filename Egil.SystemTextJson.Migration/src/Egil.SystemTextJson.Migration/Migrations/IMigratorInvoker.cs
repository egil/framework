using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

internal interface IMigratorInvoker
{
    MigratorReference Bind(TypeMetadata metadata, JsonTypeInfo sourceTypeInfo, TypeMetadata? elementMetadata, bool elementAcceptsNonObjectShapes);
}

internal abstract class MigratorInvoker<TSource, TTarget> : IMigratorInvoker
{
    public MigratorReference Bind(TypeMetadata metadata, JsonTypeInfo sourceTypeInfo, TypeMetadata? elementMetadata, bool elementAcceptsNonObjectShapes)
        => new MigratorReference<TSource, TTarget>(metadata, sourceTypeInfo, this, elementMetadata, elementAcceptsNonObjectShapes);

    public abstract bool TryMigrate(TSource source, out TTarget migrated);
}
