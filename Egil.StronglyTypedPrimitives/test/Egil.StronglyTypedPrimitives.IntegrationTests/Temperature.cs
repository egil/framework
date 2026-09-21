using Egil.StronglyTypedPrimitives;

// Relies on the generated [JsonConverter] attribute, which is the path the reflection-based tests
// exercise. STP001 is expected: this project also declares JsonSerializerContexts, and the context
// tests register StronglyTypedJsonConverterFactory instead of declaring the attribute here.
#pragma warning disable STP001

namespace Examples
{
    // The wrapped value is Celsius; Value is a computed convenience that must not leak into JSON.
    [StronglyTyped]
    public readonly partial record struct Temperature(int Celsius)
    {
        public int Value => Celsius * 9 / 5 + 32;
    }
}