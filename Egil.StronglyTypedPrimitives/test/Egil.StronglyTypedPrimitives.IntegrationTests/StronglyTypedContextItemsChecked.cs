using System.ComponentModel.DataAnnotations;
using Egil.StronglyTypedPrimitives;

// Relies on the generated [JsonConverter] attribute, which is the path the reflection-based tests
// exercise. STP001 is expected: this project also declares JsonSerializerContexts, and the context
// tests register StronglyTypedJsonConverterFactory instead of declaring the attribute here.
#pragma warning disable STP001

namespace Examples
{
    [StronglyTyped]
    public readonly partial record struct StronglyTypedContextItemsChecked([ContextItemsChecked] string Value);

    // Leaves state in the context it is given and rejects a context that already carries any, so
    // a ValidationContext reused between validations fails from the second validation on, whether
    // the calls are sequential or concurrent.
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class ContextItemsCheckedAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (validationContext.Items.Count != 0)
            {
                return new ValidationResult("The validation context carried items from another validation.");
            }

            validationContext.Items["seen"] = value;
            return ValidationResult.Success;
        }
    }
}