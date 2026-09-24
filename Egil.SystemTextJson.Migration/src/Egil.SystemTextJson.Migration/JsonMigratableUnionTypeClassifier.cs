#if NET11_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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
/// <see cref="JsonMigratableAttribute.UndiscriminatedSourceType"/>, otherwise to the single object-accepting case
/// (a non-migratable object or dictionary case, or a migratable case that migrates from a dictionary source), and
/// finally to the single migratable case (legacy payload semantics). Arrays, strings, numbers and booleans are
/// routed to the single case with that JSON shape, including migratable cases that migrate from a source of that
/// shape. A case that is a nested union or uses a converter override may accept any shape, so such a union
/// classifies discriminated payloads only.
/// Any other situation is reported as a <see cref="JsonException"/> rather than a silent pick.
/// </para>
/// </remarks>
public sealed class JsonMigratableUnionTypeClassifier : JsonTypeClassifierFactory
{
    /// <summary>
    /// Creates a classifier for migration-enabled, untrimmed applications.
    /// </summary>
    [RequiresUnreferencedCode(MigrationCompatibility.Trimming, Url = MigrationCompatibility.Url)]
    [RequiresDynamicCode(MigrationCompatibility.DynamicCode, Url = MigrationCompatibility.Url)]
    public JsonMigratableUnionTypeClassifier()
    {
    }

    /// <inheritdoc/>
    // The base virtual contract cannot acquire Requires* annotations. Construction warns even
    // without migration options; successful routing also requires annotated migration registration.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The annotated classifier constructor warns before this override can discover union cases.")]
    public override bool CanClassify(JsonTypeClassifierContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Kind is not JsonTypeClassifierKind.Union)
        {
            return false;
        }

        foreach (var unionCase in context.UnionCases)
        {
            if (JsonMigratableTypes.GetMigratableType(unionCase.CaseType) is not null || JsonMigratableTypes.IsUnionWithMigratableCase(unionCase.CaseType))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The annotated constructor and required AddJsonMigrationSupport registration warn before deferred union routing.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The annotated constructor and required AddJsonMigrationSupport registration warn before deferred union routing.")]
    public override JsonTypeClassifier CreateJsonClassifier(JsonTypeClassifierContext context, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        // The registry lives on the resolver that AddJsonMigrationSupport registers. Looking it up
        // here (instead of holding it in a field) lets the same parameterless type be used from
        // [JsonUnion(TypeClassifier = ...)] and from source-generated contexts. Each case routes
        // with the registry that built its converter (see UnionCaseRouting): a registration the
        // options no longer use can stay cached behind an application-defined resolver, and a
        // wrapper can send cases to a resolver registered on other options. The cached lookup
        // supplies the exclusions of the options being resolved and the registry for cases that
        // have no migration converter to ask, such as a case whose own converter is being built
        // through these options; it is kept as is, because inside such a build it is the
        // building registry, whichever resolver serves the other cases. Only when the options
        // carry no registration at all does the resolver serving a case stand in for it.
        MigrationScope? scope = MigrationScope.FindReachable(options);
        if (scope is null && ServingResolver(context, options) is { } serving)
        {
            scope = new MigrationScope(serving, options, []);
        }

        if (scope is null)
        {
            throw new InvalidOperationException(
                $"'{context.DeclaringType.FullName}' uses {nameof(JsonMigratableUnionTypeClassifier)}, but the serializer options have no migration support. Call options.AddJsonMigrationSupport() before serializing or deserializing this union.");
        }

        var routing = UnionCaseRouting.Build(context, scope, options);
        return routing.Classify;
    }

    private static JsonMigrationTypeInfoResolver? ServingResolver(JsonTypeClassifierContext context, JsonSerializerOptions options)
    {
        foreach (JsonUnionCaseInfo unionCase in context.UnionCases)
        {
            if (JsonMigratableTypes.GetMigratableType(unionCase.CaseType) is { } migratableType
                && options.GetTypeInfo(migratableType).Converter is IJsonMigratableConverter converter)
            {
                return converter.Resolver;
            }
        }

        return null;
    }
}
#endif
