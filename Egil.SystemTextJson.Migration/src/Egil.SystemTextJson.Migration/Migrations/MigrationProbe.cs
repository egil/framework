using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.SystemTextJson.Migration.Migrations;

/// <summary>
/// Marker type that only <see cref="JsonMigrationTypeInfoResolver"/> answers. Asking the options'
/// resolver for it reveals the migration resolver even when a decorator (for example
/// <c>WithAddedModifier</c>) hides the resolver from <see cref="JsonSerializerOptions.TypeInfoResolverChain"/>
/// or the options are a copy without a registered scope: the decorator still forwards the request,
/// and the answer carries the resolver in its converter.
/// </summary>
internal sealed class MigrationProbe
{
    private MigrationProbe()
    {
    }

    internal sealed class Converter(JsonMigrationTypeInfoResolver resolver) : JsonConverter<MigrationProbe>
    {
        public JsonMigrationTypeInfoResolver Resolver { get; } = resolver;

        public override MigrationProbe Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException($"{nameof(MigrationProbe)} is a discovery marker and is never serialized.");

        public override void Write(Utf8JsonWriter writer, MigrationProbe value, JsonSerializerOptions options)
            => throw new NotSupportedException($"{nameof(MigrationProbe)} is a discovery marker and is never serialized.");
    }
}
