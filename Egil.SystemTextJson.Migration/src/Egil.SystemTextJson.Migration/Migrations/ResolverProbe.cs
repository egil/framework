using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

/// <summary>
/// Finds the migration resolver an application-defined resolver delegates to by asking it for the
/// contract of a type only <see cref="JsonMigrationTypeInfoResolver"/> answers.
/// </summary>
/// <remarks>
/// <see cref="ResolverLeaves"/> sees through STJ's chains and decorators without calling them, but
/// an application-defined resolver is opaque to it and may wrap the migration entry. Its
/// <c>GetTypeInfo</c> is the only way to tell, so it is asked for <see cref="Marker"/>: the
/// migration resolver returns a contract it has stamped as its own. That answer is proof; any
/// other answer, or an exception a resolver raises for a type it does not know, is not proof of
/// the opposite, because a resolver that forwards only its own application's types never sees the
/// marker while still delegating every migratable contract. Callers treat silence accordingly.
/// The call is made only for such resolvers; a <see cref="DefaultJsonTypeInfoResolver"/> the
/// wrapper forwards to freezes its <c>Modifiers</c> on that call, which is the price of asking.
/// </remarks>
internal static class ResolverProbe
{
    /// <summary>
    /// The type the probe asks for. No serializer contract for it exists other than the answer.
    /// </summary>
    internal sealed class Marker;

    public static bool IsApplicationDefined(IJsonTypeInfoResolver leaf)
        => leaf is not (JsonMigrationTypeInfoResolver or JsonSerializerContext)
            && leaf.GetType() != typeof(DefaultJsonTypeInfoResolver);

    /// <summary>
    /// The migration resolver <paramref name="leaf"/> delegates to for <paramref name="options"/>,
    /// or <see langword="null"/>.
    /// </summary>
    public static JsonMigrationTypeInfoResolver? Through(IJsonTypeInfoResolver leaf, JsonSerializerOptions options)
    {
        try
        {
            return leaf.GetTypeInfo(typeof(Marker), options)?.OriginatingResolver as JsonMigrationTypeInfoResolver;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException or JsonException)
        {
            // A source-generated context rejects options other than its own, and a resolver may
            // refuse a type it has no contract for; neither says anything about migration.
            return null;
        }
    }

    /// <summary>
    /// The contract <paramref name="resolver"/> answers the probe with.
    /// </summary>
    public static JsonTypeInfo Answer(JsonMigrationTypeInfoResolver resolver, JsonSerializerOptions options)
    {
        JsonTypeInfo<Marker> answer = JsonMetadataServices.CreateValueInfo<Marker>(options, MarkerConverter.Instance);
        answer.OriginatingResolver = resolver;
        return answer;
    }

    private sealed class MarkerConverter : JsonConverter<Marker>
    {
        public static readonly MarkerConverter Instance = new();

        public override Marker Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, Marker value, JsonSerializerOptions options)
            => throw new NotSupportedException();
    }
}
