using System.Text.Json;
using System.Text.Json.Serialization;
using Orleans.Providers.Streams.Common;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class StreamSequenceTokenRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Conflicting_descriptor_preserves_original_decoder(bool differentTokenType)
    {
        var descriptor = Guid.NewGuid().ToString("N");
        StreamSequenceTokenJsonConverters.Register(descriptor, new TokenConverter());
        Action conflictingRegistration = differentTokenType
            ? () => StreamSequenceTokenJsonConverters.Register(descriptor, new V2Converter())
            : () => StreamSequenceTokenJsonConverters.Register(descriptor, new OtherConverter());

        Assert.Throws<InvalidOperationException>(conflictingRegistration);
        var restored = JsonSerializer.Deserialize<StreamCursor>(
            $$"""{"StreamNamespace":"orders","Token":{"Kind":"{{descriptor}}","Payload":7} }""");

        Assert.Equal(new EventSequenceToken(7), restored!.Token);
    }

    private class TokenConverter : JsonConverter<EventSequenceToken>
    {
        public override EventSequenceToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(reader.GetInt64());
        public override void Write(Utf8JsonWriter writer, EventSequenceToken value, JsonSerializerOptions options) => writer.WriteNumberValue(value.SequenceNumber);
    }

    private sealed class OtherConverter : TokenConverter;

    private sealed class V2Converter : JsonConverter<EventSequenceTokenV2>
    {
        public override EventSequenceTokenV2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(reader.GetInt64());
        public override void Write(Utf8JsonWriter writer, EventSequenceTokenV2 value, JsonSerializerOptions options) => writer.WriteNumberValue(value.SequenceNumber);
    }
}
