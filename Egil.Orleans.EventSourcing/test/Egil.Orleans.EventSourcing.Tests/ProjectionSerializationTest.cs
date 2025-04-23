using System.Text.Json;

namespace Egil.Orleans.EventSourcing.Tests;

public class ProjectionSerializationTest
{
    [Fact]
    public void Serialize_to_and_from_json()
    {
        var input = new Projection<Dummy>
        {
            MetadataHashCode = Projection<Dummy>.RuntimeMetadataHashCode,
            Version = 2,
            State = new Dummy("Test"),
        };

        var json = JsonSerializer.Serialize(input);
        var result = JsonSerializer.Deserialize<Projection<Dummy>>(json);

        Assert.NotNull(result);
        Assert.Equal(input.State, result.State);
        Assert.Equal(input.Version, result.Version);
        Assert.Equal(input.MetadataHashCode, result.MetadataHashCode);
    }

    [Fact]
    public void Deserializating_with_bad_state()
    {
        var result = JsonSerializer.Deserialize<Projection<Dummy>>("""
            {"Version":2,"MetadataHashCode":531920290,"State":{"Name":1234}}
            """);

        Assert.NotNull(result);
        Assert.Null(result.State);
        Assert.Equal(0, result.Version);
        Assert.Equal(0, result.MetadataHashCode);
    }

    [Fact]
    public void Deserializating_out_of_order_properties()
    {
        var result = JsonSerializer.Deserialize<Projection<Dummy>>("""
            {"Version":2,"State":{"Name":"Foo"},"MetadataHashCode":531920290}
            """);

        Assert.NotNull(result);
        Assert.Equal(new Dummy("Foo"), result.State);
        Assert.Equal(2, result.Version);
        Assert.Equal(531920290, result.MetadataHashCode);
    }

    [Fact]
    public void Deserializating_out_of_order_properties_bad_state()
    {
        var result = JsonSerializer.Deserialize<Projection<Dummy>>("""
            {"Version":2,"State":{"Name":1234},"MetadataHashCode":531920290}
            """);

        Assert.NotNull(result);
        Assert.Null(result.State);
        Assert.Equal(0, result.Version);
        Assert.Equal(0, result.MetadataHashCode);
    }

    private sealed record class Dummy(string Name);
}
