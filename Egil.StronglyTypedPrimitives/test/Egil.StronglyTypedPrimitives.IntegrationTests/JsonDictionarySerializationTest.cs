using System.Text.Json;

namespace Egil.StronglyTypedPrimitives
{
    using Examples;
    using Xunit;

    public partial class JsonDictionarySerializationTest
    {
        [Fact]
        public void JsonSerialization_with_guid_type_as_dictionary_key()
        {
            var dict = new Dictionary<StronglyTypedGuid, string>()
            {
                { new StronglyTypedGuid(Guid.NewGuid()), "value1" },
                { new StronglyTypedGuid(Guid.NewGuid()), "value2" },
            };

            var json = JsonSerializer.Serialize(dict);
            var dictFromJson = JsonSerializer.Deserialize<Dictionary<StronglyTypedGuid, string>>(json);

            Assert.Equivalent(dict.Keys, dictFromJson?.Keys);
        }

        [Fact]
        public void JsonSerialization_with_string_type_as_dictionary_key()
        {
            var dict = new Dictionary<StronglyTypedString, string>()
            {
                { new StronglyTypedString("key1"), "value1" },
                { new StronglyTypedString("key2"), "value2" },
            };

            var json = JsonSerializer.Serialize(dict);
            var dictFromJson = JsonSerializer.Deserialize<Dictionary<StronglyTypedString, string>>(json);

            Assert.Equivalent(dict.Keys, dictFromJson?.Keys);
        }

        [Fact]
        public void JsonSerialization_with_int_type_as_dictionary_key()
        {
            var dict = new Dictionary<StronglyTypedInt, string>()
            {
                { new StronglyTypedInt(1), "value1" },
                { new StronglyTypedInt(2), "value2" },
            };

            var json = JsonSerializer.Serialize(dict);
            var dictFromJson = JsonSerializer.Deserialize<Dictionary<StronglyTypedInt, string>>(json);

            Assert.Equivalent(dict.Keys, dictFromJson?.Keys);
        }

        [Fact]
        public void Decimal_dictionary_key_is_culture_invariant()
        {
            using var danishCulture = CultureScope.Use("da-DK");
            var dict = new Dictionary<StronglyTypedDecimal, string> { [new(1.5m)] = "a" };

            var json = JsonSerializer.Serialize(dict);
            var dictFromJson = JsonSerializer.Deserialize<Dictionary<StronglyTypedDecimal, string>>(json);

            Assert.Equal("""{"1.5":"a"}""", json);
            Assert.Equal(dict, dictFromJson);
        }

        [Fact]
        public void DateTime_dictionary_key_is_iso_8601_and_keeps_kind()
        {
            using var danishCulture = CultureScope.Use("da-DK");
            var key = new StronglyTypedDateTime(new DateTime(2026, 9, 17, 13, 5, 0, DateTimeKind.Utc));
            var dict = new Dictionary<StronglyTypedDateTime, string> { [key] = "a" };

            var json = JsonSerializer.Serialize(dict);
            var dictFromJson = JsonSerializer.Deserialize<Dictionary<StronglyTypedDateTime, string>>(json);

            Assert.Equal("""{"2026-09-17T13:05:00Z":"a"}""", json);
            Assert.Equal(dict, dictFromJson);
            Assert.Equal(DateTimeKind.Utc, Assert.Single(dictFromJson!.Keys).Value.Kind);
        }
    }
}