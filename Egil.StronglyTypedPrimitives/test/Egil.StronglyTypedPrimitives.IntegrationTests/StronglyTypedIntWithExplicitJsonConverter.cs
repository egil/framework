using System.Text.Json.Serialization;
using Egil.StronglyTypedPrimitives;

namespace Examples;

// Declaring the converter here, on the user-owned partial declaration, is what makes the type
// serialize correctly through a JsonSerializerContext: the System.Text.Json source generator
// only sees attributes in user code, never the ones emitted by another generator.
[StronglyTyped]
[JsonConverter(typeof(StronglyTypedJsonConverter<StronglyTypedIntWithExplicitJsonConverter, int>))]
public readonly partial record struct StronglyTypedIntWithExplicitJsonConverter(int Value);