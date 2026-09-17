using Microsoft.CodeAnalysis;

namespace Egil.StronglyTypedPrimitives;

internal static class DiagnosticDescriptors
{
    private const string Category = "Egil.StronglyTypedPrimitives";

    /// <summary>
    /// Roslyn source generators cannot see each other's output, so a JsonSerializerContext in the
    /// same compilation ignores the generated [JsonConverter] attribute and serializes the
    /// strongly typed primitive as an object with a Value property.
    /// </summary>
    public static readonly DiagnosticDescriptor JsonSerializerContextCannotSeeGeneratedConverter = new(
        id: "STP001",
        title: "JsonSerializerContext cannot see the generated JsonConverter attribute",
        messageFormat: "A JsonSerializerContext in this compilation will serialize '{0}' as an object because the System.Text.Json source generator cannot see the generated [JsonConverter] attribute. Declare [System.Text.Json.Serialization.JsonConverter(typeof(Egil.StronglyTypedPrimitives.StronglyTypedJsonConverter<{0}, {1}>))] on the partial declaration of '{0}', or register Egil.StronglyTypedPrimitives.StronglyTypedJsonConverterFactory in JsonSerializerOptions.Converters when not compiling ahead of time.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The System.Text.Json source generator only honours JsonConverter attributes written in user code, so strongly typed primitives serialized through a JsonSerializerContext must declare the converter themselves.");
}