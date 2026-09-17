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

    /// <summary>
    /// The shared converter lives in the net8.0+ assets of the Abstractions assembly only, so a
    /// consumer on the netstandard2.0 asset references System.Text.Json without being able to get
    /// the generated [JsonConverter] attribute. Reported so the missing JSON support is visible.
    /// </summary>
    public static readonly DiagnosticDescriptor JsonSupportRequiresNet8 = new(
        id: "STP002",
        title: "System.Text.Json support for strongly typed primitives requires net8.0 or later",
        messageFormat: "No JsonConverter attribute is generated for '{0}' because Egil.StronglyTypedPrimitives.StronglyTypedJsonConverter<,> is not available to this compilation. System.Text.Json support for strongly typed primitives requires targeting net8.0 or later; on earlier targets declare a JsonConverter attribute on the partial declaration of '{0}' yourself.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The shared StronglyTypedJsonConverter ships only in the net8.0 and later assets of the package, so compilations that use the netstandard2.0 asset get no generated JSON support.");
}