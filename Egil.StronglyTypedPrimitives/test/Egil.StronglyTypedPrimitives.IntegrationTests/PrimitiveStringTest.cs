using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.StronglyTypedPrimitives
{
    using Examples;
    using System;
    using Xunit;

    public partial class PrimitiveStringTest
    {
        [Fact]
        public void StronglyTyped_string_methods_constrained()
        {
            var tooShortString = "12345";
            var tooShortSpan = tooShortString.AsSpan();
            var goodString = "123456";
            var goodSpan = goodString.AsSpan();

            Assert.Equal(StronglyTypedStringWithConstraints.Empty, default(StronglyTypedStringWithConstraints));
            Assert.NotEqual(StronglyTypedStringWithConstraints.Empty, new StronglyTypedStringWithConstraints(goodString));
            Assert.Equal(string.Empty, StronglyTypedStringWithConstraints.Empty.ToString());

            Assert.Throws<ArgumentException>(() => new StronglyTypedStringWithConstraints(tooShortString));
            Assert.Throws<ArgumentException>(() => StronglyTypedStringWithConstraints.Empty with { Value = tooShortString });

            Assert.Throws<ArgumentException>(() => StronglyTypedStringWithConstraints.Parse(tooShortString, null));
            Assert.Equal(new StronglyTypedStringWithConstraints(goodString), StronglyTypedStringWithConstraints.Parse(goodString, null));

            Assert.False(StronglyTypedStringWithConstraints.TryParse(tooShortString, null, out var _));
            Assert.True(StronglyTypedStringWithConstraints.TryParse(goodString, null, out var tryParsedString));
            Assert.Equal(new StronglyTypedStringWithConstraints(goodString), tryParsedString);

            Assert.Throws<ArgumentException>(() => StronglyTypedStringWithConstraints.Parse(tooShortString, null));
            Assert.Equal(new StronglyTypedStringWithConstraints(goodString), StronglyTypedStringWithConstraints.Parse(goodString, null));

            Assert.False(StronglyTypedStringWithConstraints.TryParse(tooShortSpan, null, out var _));
            Assert.True(StronglyTypedStringWithConstraints.TryParse(goodSpan, null, out var tryParsedSpan));
            Assert.Equal(new StronglyTypedStringWithConstraints(goodString), tryParsedSpan);

            Assert.Equal(goodString, new StronglyTypedStringWithConstraints(goodString).ToString());
            Assert.Equal("bbbbbb".CompareTo("cbbbbb"), new StronglyTypedStringWithConstraints("bbbbbb").CompareTo(new StronglyTypedStringWithConstraints("cbbbbb")));
            Assert.True(new StronglyTypedStringWithConstraints(goodString) == new StronglyTypedStringWithConstraints(goodString));
            Assert.False(new StronglyTypedStringWithConstraints(goodString) != new StronglyTypedStringWithConstraints(goodString));
            Assert.True(new StronglyTypedStringWithConstraints("bbbbbb") < new StronglyTypedStringWithConstraints("cbbbbb"));
            Assert.False(new StronglyTypedStringWithConstraints("bbbbbb") > new StronglyTypedStringWithConstraints("cbbbbb"));
            Assert.True(new StronglyTypedStringWithConstraints("cbbbbb") <= new StronglyTypedStringWithConstraints("cbbbbb"));
            Assert.True(new StronglyTypedStringWithConstraints("cbbbbb") >= new StronglyTypedStringWithConstraints("cbbbbb"));
            Assert.Equal("bbbbbb".CompareTo("cbbbbb"), new StronglyTypedStringWithConstraints("bbbbbb").CompareTo("cbbbbb"));
            Assert.Throws<ArgumentException>(() => new StronglyTypedStringWithConstraints("bbbbbb").CompareTo(42));

            Assert.Equal(new StronglyTypedStringWithConstraints(goodString), JsonSerializer.Deserialize<StronglyTypedStringWithConstraints>(JsonSerializer.Serialize(new StronglyTypedStringWithConstraints(goodString))));
            Assert.Equal(StronglyTypedStringWithConstraints.Empty, JsonSerializer.Deserialize<StronglyTypedStringWithConstraints>($"\"{tooShortString}\""));
            Assert.Equal(StronglyTypedStringWithConstraints.Empty, JsonSerializer.Deserialize<StronglyTypedStringWithConstraints>("null"));
            Assert.Equal($"\"{goodString}\"", JsonSerializer.Serialize(new StronglyTypedStringWithConstraints(goodString)));
            Assert.Equal(new StronglyTypedStringWithConstraints(goodString), JsonSerializer.Deserialize<StronglyTypedStringWithConstraints>($"\"{goodString}\""));

            Assert.False(new StronglyTypedStringWithConstraints(goodString) == default);
            Assert.True(new StronglyTypedStringWithConstraints(goodString) != default);
            Assert.False(new StronglyTypedStringWithConstraints(goodString) == StronglyTypedStringWithConstraints.Empty);
            Assert.True(new StronglyTypedStringWithConstraints(goodString) != StronglyTypedStringWithConstraints.Empty);
            Assert.False(new StronglyTypedStringWithConstraints(goodString) < StronglyTypedStringWithConstraints.Empty);
            Assert.True(new StronglyTypedStringWithConstraints(goodString) > StronglyTypedStringWithConstraints.Empty);
            Assert.False(new StronglyTypedStringWithConstraints(goodString) <= StronglyTypedStringWithConstraints.Empty);
            Assert.True(new StronglyTypedStringWithConstraints(goodString) >= StronglyTypedStringWithConstraints.Empty);
        }

        [Fact]
        public void StronglyTyped_string_methods()
        {
            var goodString = "123456";
            var goodSpan = goodString.AsSpan();

            Assert.Equal(StronglyTypedString.Empty, default(StronglyTypedString));
            Assert.NotEqual(StronglyTypedString.Empty, new StronglyTypedString(goodString));
            Assert.Equal(string.Empty, StronglyTypedString.Empty.ToString());

            Assert.Equal(new StronglyTypedString(goodString), StronglyTypedString.Parse(goodString, null));

            Assert.True(StronglyTypedString.TryParse(goodString, null, out var tryParsedString));
            Assert.Equal(new StronglyTypedString(goodString), tryParsedString);

            Assert.Equal(new StronglyTypedString(goodString), StronglyTypedString.Parse(goodString, null));

            Assert.True(StronglyTypedString.TryParse(goodSpan, null, out var tryParsedSpan));
            Assert.Equal(new StronglyTypedString(goodString), tryParsedSpan);

            Assert.Equal(goodString, new StronglyTypedString(goodString).ToString());
            Assert.Equal("bbbbbb".CompareTo("cbbbbb"), new StronglyTypedString("bbbbbb").CompareTo(new StronglyTypedString("cbbbbb")));
            Assert.True(new StronglyTypedString(goodString) == new StronglyTypedString(goodString));
            Assert.False(new StronglyTypedString(goodString) != new StronglyTypedString(goodString));
            Assert.True(new StronglyTypedString("bbbbbb") < new StronglyTypedString("cbbbbb"));
            Assert.False(new StronglyTypedString("bbbbbb") > new StronglyTypedString("cbbbbb"));
            Assert.True(new StronglyTypedString("cbbbbb") <= new StronglyTypedString("cbbbbb"));
            Assert.True(new StronglyTypedString("cbbbbb") >= new StronglyTypedString("cbbbbb"));
            Assert.Equal("bbbbbb".CompareTo("cbbbbb"), new StronglyTypedString("bbbbbb").CompareTo("cbbbbb"));
            Assert.Throws<ArgumentException>(() => new StronglyTypedString("bbbbbb").CompareTo(42));

            Assert.Equal(new StronglyTypedString(goodString), JsonSerializer.Deserialize<StronglyTypedString>(JsonSerializer.Serialize(new StronglyTypedString(goodString))));
            Assert.Equal(StronglyTypedString.Empty, JsonSerializer.Deserialize<StronglyTypedString>("null"));
            Assert.Equal($"\"{goodString}\"", JsonSerializer.Serialize(new StronglyTypedString(goodString)));
            Assert.Equal(new StronglyTypedString(goodString), JsonSerializer.Deserialize<StronglyTypedString>($"\"{goodString}\""));

            Assert.False(new StronglyTypedString(goodString) == default);
            Assert.True(new StronglyTypedString(goodString) != default);
            Assert.False(new StronglyTypedString(goodString) == StronglyTypedString.Empty);
            Assert.True(new StronglyTypedString(goodString) != StronglyTypedString.Empty);
            Assert.False(new StronglyTypedString(goodString) < StronglyTypedString.Empty);
            Assert.True(new StronglyTypedString(goodString) > StronglyTypedString.Empty);
            Assert.False(new StronglyTypedString(goodString) <= StronglyTypedString.Empty);
            Assert.True(new StronglyTypedString(goodString) >= StronglyTypedString.Empty);
        }

        [Fact]
        public void JsonSerialization_with_type_resolver()
        {
            var options = new JsonSerializerOptions { TypeInfoResolver = TestJsonSerializerContext.Default };
            var dto = new Dto(new TypedId("42"), new StronglyTypedString("Foo"), [new("Bar"), new("Baz")]);

            var json = JsonSerializer.Serialize(dto, options);
            var dtoFromJson = JsonSerializer.Deserialize<Dto>(json, options);

            Assert.Equivalent(dto, dtoFromJson);
        }
    }

    [StronglyTyped]
    public readonly partial record struct TypedId(string Id);

    public record class Dto(TypedId Id, StronglyTypedString Name, IEnumerable<StronglyTypedString> PetNames);

    [JsonSourceGenerationOptions(
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IgnoreReadOnlyFields = true,
        RespectNullableAnnotations = true,
        UseStringEnumConverter = true,
        AllowOutOfOrderMetadataProperties = true)]
    [JsonSerializable(typeof(Dto))]
    internal sealed partial class TestJsonSerializerContext : JsonSerializerContext
    {
    }
}
