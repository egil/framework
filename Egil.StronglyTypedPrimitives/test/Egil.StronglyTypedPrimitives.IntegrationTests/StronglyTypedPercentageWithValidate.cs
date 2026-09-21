using System.ComponentModel.DataAnnotations;
using Egil.StronglyTypedPrimitives;

// Relies on the generated [JsonConverter] attribute, which is the path the reflection-based tests
// exercise. STP001 is expected: this project also declares JsonSerializerContexts, and the context
// tests register StronglyTypedJsonConverterFactory instead of declaring the attribute here.
#pragma warning disable STP001

namespace Examples
{
    // The range admits the default value on purpose: it is the shape where an invalid request
    // body cannot be told apart from a valid default after conversion.
    [StronglyTyped]
    public readonly partial record struct StronglyTypedPercentageWithValidate([Range(0, 100)] int Value) : IValidatableObject;
}