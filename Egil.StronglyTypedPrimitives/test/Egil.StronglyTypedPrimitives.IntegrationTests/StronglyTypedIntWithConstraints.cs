using Egil.StronglyTypedPrimitives;

// Relies on the generated [JsonConverter] attribute, which is the path the reflection-based tests
// exercise. STP001 is expected: this project also declares JsonSerializerContexts, and the context
// tests register StronglyTypedJsonConverterFactory instead of declaring the attribute here.
#pragma warning disable STP001

namespace Examples
{
    [StronglyTyped]
    public readonly partial record struct StronglyTypedIntWithConstraints(int Value)
    {
        public static bool IsValueValid(int value, bool throwIfInvalid)
        {
            if (value > 5)
                return true;

            if (throwIfInvalid)
                throw new ArgumentException("Value must be at larger than 5", nameof(value));

            return false;
        }
    }
}