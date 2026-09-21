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
    /// A user-written IsValueValid always wins over the generated one, so validation attributes on
    /// the positional parameter are never evaluated when the method is declared by hand.
    /// </summary>
    public static readonly DiagnosticDescriptor ValidationAttributesIgnoredByUserIsValueValid = new(
        id: "STP002",
        title: "Validation attributes are ignored when IsValueValid is declared",
        messageFormat: "The validation attribute '{0}' on '{1}' has no effect because '{2}' declares its own IsValueValid method. Remove the attribute, or remove the method to let the generator validate the value with the attributes.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The generator only emits an IsValueValid that evaluates the validation attributes when the type does not declare the method itself.");

    /// <summary>
    /// The value invariant is checked synchronously in constructors, init accessors, Parse and the
    /// JSON converter, so an attribute that can only validate asynchronously cannot be part of it.
    /// The only generated member that can await it is ValidateAsync, which exists when the type
    /// declares IAsyncValidatableObject. The warning is about this generator's output only:
    /// ASP.NET Core validation still evaluates the attribute on the compiler-synthesized property.
    /// </summary>
    public static readonly DiagnosticDescriptor AsyncValidationAttributeNotPartOfValueInvariant = new(
        id: "STP003",
        title: "Async validation attributes are not part of the value invariant",
        messageFormat: "The async validation attribute '{0}' on '{1}' is not part of the value invariant of '{2}' because async validation cannot run where the invariant is checked, so nothing this generator emits evaluates it. ASP.NET Core validation still evaluates it on the '{1}' property; for it to run through ValidateAsync, for example under Validator.TryValidateObjectAsync, declare System.ComponentModel.DataAnnotations.IAsyncValidatableObject on the partial declaration of '{2}'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Attributes deriving from AsyncValidationAttribute cannot be part of the synchronous value invariant, so no generated member evaluates them unless the type declares IAsyncValidatableObject; the generator then evaluates them in ValidateAsync.");

    /// <summary>
    /// The shared converter lives in the net8.0+ assets of the Abstractions assembly only, so a
    /// consumer on the netstandard2.0 asset references System.Text.Json without being able to get
    /// the generated [JsonConverter] attribute. Reported so the missing JSON support is visible.
    /// </summary>
    public static readonly DiagnosticDescriptor JsonSupportRequiresNet8 = new(
        id: "STP004",
        title: "System.Text.Json support for strongly typed primitives requires net8.0 or later",
        messageFormat: "No JsonConverter attribute is generated for '{0}' because Egil.StronglyTypedPrimitives.StronglyTypedJsonConverter<,> is not available to this compilation. System.Text.Json support for strongly typed primitives requires targeting net8.0 or later; on earlier targets declare a JsonConverter attribute on the partial declaration of '{0}' yourself.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The shared StronglyTypedJsonConverter ships only in the net8.0 and later assets of the package, so compilations that use the netstandard2.0 asset get no generated JSON support.");

    /// <summary>
    /// The value invariant validates a bare value against a placeholder context whose
    /// ObjectInstance is a sentinel and which carries no services or items. An attribute that
    /// overrides RequiresValidationContext (CustomValidationAttribute among the built-in ones)
    /// declares that it needs the real thing, so it is left to IValidatableObject.Validate, which
    /// is handed one.
    /// </summary>
    public static readonly DiagnosticDescriptor ContextValidationAttributeNotPartOfValueInvariant = new(
        id: "STP005",
        title: "Validation attributes that require a ValidationContext are not part of the value invariant",
        messageFormat: "The validation attribute '{0}' on '{1}' is not part of the value invariant of '{2}' because it requires a ValidationContext. It is evaluated only through IValidatableObject.Validate when '{2}' declares that interface.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Attributes that override RequiresValidationContext, such as CustomValidationAttribute, are excluded from the generated IsValueValid, which validates the bare value against a placeholder context without an object instance, services or items.");
}