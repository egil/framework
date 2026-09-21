using System.ComponentModel.DataAnnotations;
using Egil.StronglyTypedPrimitives;

// Relies on the generated [JsonConverter] attribute, which is the path the reflection-based tests
// exercise. STP001 is expected: this project also declares JsonSerializerContexts, and the context
// tests register StronglyTypedJsonConverterFactory instead of declaring the attribute here.
#pragma warning disable STP001

namespace Examples
{
    // The static initializers below go through the constructor, and with it through the generated
    // IsValueValid, while the type itself is still being initialized. They exist to prove the
    // generated validators are usable at that point regardless of which partial declaration the
    // compiler initializes first.
    [StronglyTyped]
    public readonly partial record struct StronglyTypedQuantity([Range(1, 100)] int Value)
    {
        public static readonly StronglyTypedQuantity Default = new(1);
    }

    [StronglyTyped]
    public readonly partial record struct StronglyTypedPositiveInt([Range(1, int.MaxValue)] int Value)
    {
        public static readonly StronglyTypedPositiveInt Empty = new(1);
    }
}