using System.Text;
using Orleans.Serialization;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class StreamManagerStreamIdTests
{
    [Fact]
    public void Derived_stream_ids_preserve_compound_grain_extensions()
    {
        var grainType = GrainType.Create("test.compound-grain");
        var grainKey = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var tenantA = GrainId.Create(
            grainType,
            GrainIdKeyExtensions.CreateGuidKey(grainKey, "tenant-a"));
        var tenantB = GrainId.Create(
            grainType,
            GrainIdKeyExtensions.CreateGuidKey(grainKey, "tenant-b"));

        var tenantAStream = StreamManager.CreateStreamId("orders", tenantA);
        var tenantBStream = StreamManager.CreateStreamId("orders", tenantB);

        Assert.Equal(tenantA.ToString(), tenantAStream.GetKeyAsString());
        Assert.Equal(tenantB.ToString(), tenantBStream.GetKeyAsString());
        Assert.NotEqual(tenantAStream, tenantBStream);
    }

    [Fact]
    public void Derived_stream_ids_preserve_case_sensitive_string_keys()
    {
        var grainType = GrainType.Create("test.string-grain");
        var lowerCase = GrainId.Create(grainType, "a");
        var upperCase = GrainId.Create(grainType, "A");

        var lowerCaseStream = StreamManager.CreateStreamId("orders", lowerCase);
        var upperCaseStream = StreamManager.CreateStreamId("orders", upperCase);

        Assert.Equal(lowerCase.ToString(), lowerCaseStream.GetKeyAsString());
        Assert.Equal(upperCase.ToString(), upperCaseStream.GetKeyAsString());
        Assert.NotEqual(lowerCaseStream, upperCaseStream);
    }

    [Fact]
    public void Derived_stream_ids_distinguish_integer_and_hex_parseable_string_grains()
    {
        var integerGrain = GrainId.Create(
            GrainType.Create("test.integer-grain"),
            GrainIdKeyExtensions.CreateIntegerKey(10));
        var stringGrain = GrainId.Create(GrainType.Create("test.string-grain"), "A");

        var integerStream = StreamManager.CreateStreamId("orders", integerGrain);
        var stringStream = StreamManager.CreateStreamId("orders", stringGrain);

        Assert.Equal(integerGrain.ToString(), integerStream.GetKeyAsString());
        Assert.Equal(stringGrain.ToString(), stringStream.GetKeyAsString());
        Assert.NotEqual(integerStream, stringStream);
    }

    [Fact]
    public void Derived_stream_id_survives_Orleans_serialization()
    {
        var grainId = GrainId.Create(
            GrainType.Create("test.compound-grain"),
            GrainIdKeyExtensions.CreateIntegerKey(42, "tenant-a"));
        var streamId = StreamManager.CreateStreamId("orders", grainId);
        var services = new ServiceCollection();
        services.AddSerializer(builder => builder.AddAssembly(typeof(StreamManager).Assembly));
        using var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<Serializer>();

        var serialized = serializer.SerializeToArray(streamId);
        var deserialized = serializer.Deserialize<StreamId>(serialized);

        Assert.Equal(streamId, deserialized);
        Assert.Equal("orders", deserialized.GetNamespace());
        Assert.True(GrainId.TryParse(deserialized.GetKeyAsString(), out var parsed));
        Assert.Equal(grainId, parsed);
    }

    [Fact]
    public void Derived_stream_id_survives_Orleans_text_round_trip()
    {
        var grainId = GrainId.Create(
            GrainType.Create("test.compound-grain"),
            GrainIdKeyExtensions.CreateGuidKey(
                Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
                "tenant-a"));
        var streamId = StreamManager.CreateStreamId("orders", grainId);

        var parsed = StreamId.Parse(Encoding.UTF8.GetBytes(streamId.ToString()));

        Assert.Equal(streamId, parsed);
    }

    [Fact]
    public void Derived_stream_id_rejects_grain_identity_that_cannot_round_trip_as_text()
    {
        var grainId = GrainId.Create(GrainType.Create("custom/type"), "one");

        var exception = Assert.Throws<ArgumentException>(
            () => StreamManager.CreateStreamId("orders", grainId));

        Assert.Equal("grainId", exception.ParamName);
        Assert.Contains("Provide an explicit StreamId instead.", exception.Message);
    }
}
