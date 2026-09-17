using System.Text.Json;
using System.Text.Json.Serialization;
using Orleans.Providers.Streams.Common;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class StreamSequenceTokenRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Conflicting_descriptor_preserves_original_decoder(bool differentTokenType)
    {
        var descriptor = NewDescriptor();
        StreamSequenceTokenJsonConverters.Register(descriptor, new TokenConverter());
        Action conflictingRegistration = differentTokenType
            ? () => StreamSequenceTokenJsonConverters.Register(descriptor, new V2Converter())
            : () => StreamSequenceTokenJsonConverters.Register(descriptor, new OtherConverter());

        Assert.Throws<InvalidOperationException>(conflictingRegistration);

        Assert.Equal(new EventSequenceToken(7), Decode(descriptor, "7"));
    }

    [Fact]
    public void Identical_registration_is_idempotent()
    {
        var descriptor = NewDescriptor();
        StreamSequenceTokenJsonConverters.Register(descriptor, new TokenConverter());

        StreamSequenceTokenJsonConverters.Register(descriptor, new TokenConverter());

        Assert.Equal(new EventSequenceToken(7), Decode(descriptor, "7"));
    }

    [Fact]
    public void TryRegister_adds_a_new_descriptor()
    {
        var descriptor = NewDescriptor();

        var registered = StreamSequenceTokenJsonConverters.TryRegister(descriptor, new TokenConverter());

        Assert.True(registered);
        Assert.Equal(new EventSequenceToken(7), Decode(descriptor, "7"));
    }

    [Fact]
    public void TryRegister_accepts_an_identical_registration()
    {
        var descriptor = NewDescriptor();
        StreamSequenceTokenJsonConverters.Register(descriptor, new TokenConverter());

        var registered = StreamSequenceTokenJsonConverters.TryRegister(descriptor, new TokenConverter());

        Assert.True(registered);
        Assert.Equal(new EventSequenceToken(7), Decode(descriptor, "7"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryRegister_reports_a_conflicting_converter(bool differentTokenType)
    {
        var descriptor = NewDescriptor();
        StreamSequenceTokenJsonConverters.Register(descriptor, new TokenConverter());

        var registered = differentTokenType
            ? StreamSequenceTokenJsonConverters.TryRegister(descriptor, new V2Converter())
            : StreamSequenceTokenJsonConverters.TryRegister(descriptor, new OtherConverter());

        Assert.False(registered);
        Assert.Equal(new EventSequenceToken(7), Decode(descriptor, "7"));
    }

    [Fact]
    public void TryRegister_constructs_a_parameterless_converter()
    {
        var descriptor = NewDescriptor();

        var registered = StreamSequenceTokenJsonConverters
            .TryRegister<EventSequenceToken, TokenConverter>(descriptor);

        Assert.True(registered);
        Assert.Equal(new EventSequenceToken(7), Decode(descriptor, "7"));
    }

    // The registry is process-wide and exposes no reset or enumeration API, so every
    // test claims a descriptor no other test can collide with. Registering a different
    // converter under a real descriptor would poison the rest of the run.
    private static string NewDescriptor() => Guid.NewGuid().ToString("N");

    private static StreamSequenceToken? Decode(string descriptor, string payload) =>
        JsonSerializer.Deserialize<StreamCursor>(
            $$"""{"StreamNamespace":"orders","Token":{"Kind":"{{descriptor}}","Payload":{{payload}}} }""")!.Token;

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
