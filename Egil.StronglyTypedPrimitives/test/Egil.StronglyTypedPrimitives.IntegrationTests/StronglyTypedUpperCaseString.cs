using System.ComponentModel.DataAnnotations;
using Egil.StronglyTypedPrimitives;

// Relies on the generated [JsonConverter] attribute, which is the path the reflection-based tests
// exercise. STP001 is expected: this project also declares JsonSerializerContexts, and the context
// tests register StronglyTypedJsonConverterFactory instead of declaring the attribute here.
#pragma warning disable STP001

namespace Examples
{
    [StronglyTyped]
    public readonly partial record struct StronglyTypedUpperCaseString([UpperCase] string Value);

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class UpperCaseAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
            => value is not string text || !text.Any(char.IsLower);

        public override string FormatErrorMessage(string name)
            => $"The {name} field must be upper case.";
    }
}
