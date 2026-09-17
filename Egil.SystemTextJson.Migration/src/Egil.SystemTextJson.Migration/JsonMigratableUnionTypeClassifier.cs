#if NET11_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
using Egil.SystemTextJson.Migration.Migrations;

namespace Egil.SystemTextJson.Migration;

/// <summary>
/// Classifies C# union payloads by migration type discriminators so that every case annotated with
/// <see cref="JsonMigratableAttribute"/> receives both its current payloads and the payloads of the
/// source types that migrate into it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="JsonMigrationSerializerOptionsExtensions.AddJsonMigrationSupport(JsonSerializerOptions, Action{JsonMigrationBuilder}?)"/>
/// registers this classifier in <see cref="JsonSerializerOptions.TypeClassifiers"/>, so reflection-based
/// serialization needs no further configuration. Source-generated contexts must name it explicitly on the
/// union with <c>[JsonUnion(TypeClassifier = typeof(JsonMigratableUnionTypeClassifier))]</c> because the
/// generator rejects unions whose cases share a JSON value type unless a classifier is declared.
/// </para>
/// <para>
/// Object payloads are routed by their first property: a known discriminator selects the case it belongs to.
/// Without a recognized leading discriminator, the payload goes to the single case that declares
/// <see cref="JsonMigratableAttribute.UndiscriminatedSourceType"/>, otherwise to the single case whose contract
/// is a JSON object and that is not migratable, and finally to the single migratable case (legacy payload
/// semantics). Arrays, strings, numbers and booleans are routed to the single case with that JSON shape.
/// Any other situation is reported as a <see cref="JsonException"/> rather than a silent pick.
/// </para>
/// </remarks>
public sealed class JsonMigratableUnionTypeClassifier : JsonTypeClassifierFactory
{
    /// <inheritdoc/>
    public override bool CanClassify(JsonTypeClassifierContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Kind is not JsonTypeClassifierKind.Union)
        {
            return false;
        }

        foreach (var unionCase in context.UnionCases)
        {
            if (JsonMigratableTypes.IsMigratable(unionCase.CaseType))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public override JsonTypeClassifier CreateJsonClassifier(JsonTypeClassifierContext context, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        // The registry lives on the converter factory that AddJsonMigrationSupport registers. Looking it
        // up here (instead of holding it in a field) lets the same parameterless type be used from
        // [JsonUnion(TypeClassifier = ...)] and from source-generated contexts.
        JsonMigrationRegistry? registry = null;
        foreach (JsonConverter converter in options.Converters)
        {
            if (converter is JsonMigratableConverterFactory factory)
            {
                registry = factory.Registry;
                break;
            }
        }

        if (registry is null)
        {
            throw new InvalidOperationException(
                $"'{context.DeclaringType.FullName}' uses {nameof(JsonMigratableUnionTypeClassifier)}, but the serializer options have no migration support. Call options.AddJsonMigrationSupport() before serializing or deserializing this union.");
        }

        var routing = UnionCaseRouting.Build(context, registry, options);
        return routing.Classify;
    }
}
#endif
