using System.ComponentModel.DataAnnotations;
using Egil.StronglyTypedPrimitives;

// Relies on the generated [JsonConverter] attribute, which is the path the reflection-based tests
// exercise. STP001 is expected: this project also declares JsonSerializerContexts, and the context
// tests register StronglyTypedJsonConverterFactory instead of declaring the attribute here.
#pragma warning disable STP001

namespace Examples
{
    [StronglyTyped]
    public readonly partial record struct StronglyTypedNotBlank([NotBlank] string Value);

    // Overrides only the context-taking IsValid and reads the context. The single-argument
    // IsValid the base class offers forwards a null context to this override, so the generated
    // IsValueValid has to call the attribute with a real context for the type to be usable.
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class NotBlankAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
            => value is string text && text.Trim().Length == 0
                ? new ValidationResult($"The {validationContext.MemberName} field must not be blank.")
                : ValidationResult.Success;
    }
}