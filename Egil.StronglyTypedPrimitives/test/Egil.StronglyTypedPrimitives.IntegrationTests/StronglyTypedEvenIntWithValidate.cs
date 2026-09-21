using System.ComponentModel.DataAnnotations;
using Egil.StronglyTypedPrimitives;

// Relies on the generated [JsonConverter] attribute, which is the path the reflection-based tests
// exercise. STP001 is expected: this project also declares JsonSerializerContexts, and the context
// tests register StronglyTypedJsonConverterFactory instead of declaring the attribute here.
#pragma warning disable STP001
// STP005 is expected too: CustomValidation needs a ValidationContext, so it is left out of the
// value invariant and evaluated by the generated Validate, which is what this type exists to show.
#pragma warning disable STP005

namespace Examples
{
    [StronglyTyped]
    public readonly partial record struct StronglyTypedEvenIntWithValidate([CustomValidation(typeof(EvenRules), nameof(EvenRules.Validate))] int Value) : IValidatableObject;

    public static class EvenRules
    {
        public static ValidationResult? Validate(int value, ValidationContext context)
            => value % 2 == 0 ? ValidationResult.Success : new ValidationResult($"{context.DisplayName} must be even.");
    }
}