using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.StronglyTypedPrimitives.AotCompatibility;

// The AOT tier documented in the README: the converter is declared on the user-owned partial
// declaration so the JsonSerializerContext below can see it. The generator emits nothing
// JSON-related for these types.
[StronglyTyped]
[JsonConverter(typeof(StronglyTypedJsonConverter<OrderId, int>))]
public readonly partial record struct OrderId(int Value);

[StronglyTyped]
[JsonConverter(typeof(StronglyTypedJsonConverter<Price, decimal>))]
public readonly partial record struct Price(decimal Value);

// Validation attributes make the generator emit a nested class with a static field per attribute
// and an IsValueValid that calls IsValid and FormatErrorMessage on them; this proves that code is
// trim/AOT clean too.
[StronglyTyped]
[JsonConverter(typeof(StronglyTypedJsonConverter<CustomerEmail, string>))]
public readonly partial record struct CustomerEmail([EmailAddress, StringLength(254, MinimumLength = 3)] string Value);

// Relies on the generated [JsonConverter] attribute instead, so the build also proves that the
// generated attribute itself is trim/AOT clean. STP001 is expected here: a JsonSerializerContext
// exists in this compilation and would not see the generated attribute, which is exactly why
// the type is kept out of the context's object graph.
#pragma warning disable STP001
[StronglyTyped]
public readonly partial record struct CustomerName(string Value);
#pragma warning restore STP001

public sealed record Order(OrderId Id, Dictionary<Price, string> Lines);

[JsonSerializable(typeof(Order))]
internal sealed partial class OrderJsonContext : JsonSerializerContext;

public static class OrderSerializer
{
    public static string Serialize(Order order)
        => JsonSerializer.Serialize(order, OrderJsonContext.Default.Order);

    public static Order? Deserialize(string json)
        => JsonSerializer.Deserialize(json, OrderJsonContext.Default.Order);
}