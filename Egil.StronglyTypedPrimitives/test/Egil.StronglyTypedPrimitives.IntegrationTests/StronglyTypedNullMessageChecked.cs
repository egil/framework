using System.ComponentModel.DataAnnotations;
using Egil.StronglyTypedPrimitives;

// Relies on the generated [JsonConverter] attribute, which is the path the reflection-based tests
// exercise. STP001 is expected: this project also declares JsonSerializerContexts, and the context
// tests register StronglyTypedJsonConverterFactory instead of declaring the attribute here.
#pragma warning disable STP001

namespace Examples
{
    [StronglyTyped]
    public readonly partial record struct StronglyTypedNullMessageChecked([NoMessage] string Value);

    // Fails without ever producing an error message: the result carries a null ErrorMessage and
    // the FormatErrorMessage fallback that GetValidationResult uses for an empty one returns null
    // as well. The generated IsValueValid must still treat that as a failure.
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class NoMessageAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
            => value is "ok" ? ValidationResult.Success : new ValidationResult(null);

        public override string FormatErrorMessage(string name) => null!;
    }
}