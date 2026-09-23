using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

/// <summary>
/// A <see cref="MigratorReference"/> seen from its target, so <see cref="JsonMigratableConverter{T}"/>
/// can read and migrate a source without knowing the source type.
/// </summary>
internal abstract record MigratorReference<TTarget>(
    Type SourceType,
    TypeMetadata SourceMetadata,
    JsonTypeInfo SourceTypeInfo,
    TypeMetadata? ElementMetadata,
    bool ElementAcceptsNonObjectShapes)
    : MigratorReference(SourceType, SourceMetadata, SourceTypeInfo, ElementMetadata, ElementAcceptsNonObjectShapes)
{
    public abstract bool TryRead(ref Utf8JsonReader reader, [MaybeNullWhen(false)] out TTarget migrated);
}

internal sealed record MigratorReference<TSource, TTarget>(
    TypeMetadata SourceMetadata,
    JsonTypeInfo SourceTypeInfo,
    MigratorInvoker<TSource, TTarget> Invoker,
    TypeMetadata? ElementMetadata,
    bool ElementAcceptsNonObjectShapes)
    : MigratorReference<TTarget>(typeof(TSource), SourceMetadata, SourceTypeInfo, ElementMetadata, ElementAcceptsNonObjectShapes)
{
    private readonly JsonConverter<TSource>? converter = SourceTypeInfo.Converter as JsonConverter<TSource>;
    private readonly JsonTypeInfo<TSource> typeInfo = (JsonTypeInfo<TSource>)SourceTypeInfo;

    public override bool TryRead(ref Utf8JsonReader reader, [MaybeNullWhen(false)] out TTarget migrated)
    {
        // Calling the converter's Read directly skips the ReadStack that carries JsonNumberHandling,
        // so a quoted number would fail in the plain numeric converter. That case takes the full
        // serializer path instead; it only occurs for top-level primitive sources with
        // AllowReadingFromString, so the happy path keeps the direct call. The serializer path also
        // serves a source converter declared for a base type (such as JsonConverter<object>), which
        // is not a JsonConverter<TSource> but which STJ adapts to TSource there.
        TSource? source = converter is null || (reader.TokenType is JsonTokenType.String && SourceShape is SourceValueShape.Number)
            ? JsonSerializer.Deserialize(ref reader, typeInfo)
            : converter.Read(ref reader, typeof(TSource), typeInfo.Options);

        if (source is not null && Invoker.TryMigrate(source, out migrated) && migrated is not null)
        {
            return true;
        }

        migrated = default;
        return false;
    }
}
