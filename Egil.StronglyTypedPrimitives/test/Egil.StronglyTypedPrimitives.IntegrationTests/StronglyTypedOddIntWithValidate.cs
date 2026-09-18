using System.ComponentModel.DataAnnotations;
using Egil.StronglyTypedPrimitives;

// Relies on the generated [JsonConverter] attribute, which is the path the reflection-based tests
// exercise. STP001 is expected: this project also declares JsonSerializerContexts, and the context
// tests register StronglyTypedJsonConverterFactory instead of declaring the attribute here.
#pragma warning disable STP001

namespace Examples
{
    // IsValueValid never throws, so the generated Validate has no exception message to report
    // and falls back to its own.
    [StronglyTyped]
    public readonly partial record struct StronglyTypedOddIntWithValidate(int Value) : IValidatableObject
    {
        public static bool IsValueValid(int value, bool throwIfInvalid)
            => value % 2 != 0;
    }
}