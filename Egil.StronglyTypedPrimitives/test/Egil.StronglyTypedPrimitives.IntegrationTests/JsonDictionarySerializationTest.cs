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
    }
}