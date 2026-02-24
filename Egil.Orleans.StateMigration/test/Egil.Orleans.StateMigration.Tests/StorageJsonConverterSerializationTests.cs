using System.Text;
using System.Text.Json;

namespace Egil.Orleans.StateMigration.Tests;

public sealed class StorageJsonConverterSerializationTests
{
    [Fact]
    public void Serialization_writes_type_metadata_as_the_first_property()
    {
        var value = new Storage<NonAliasedState>
        {
            Value = new NonAliasedState { Name = "alice" },
        };

        string json = JsonSerializer.Serialize(value);

        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        Assert.True(reader.Read());
        Assert.Equal(JsonTokenType.StartObject, reader.TokenType);
        Assert.True(reader.Read());
        Assert.Equal(JsonTokenType.PropertyName, reader.TokenType);
        Assert.Equal("$type", reader.GetString());
    }

    [Fact]
    public void Serialization_uses_orleans_alias_when_present()
    {
        var value = new Storage<AliasedState>
        {
            Value = new AliasedState { Name = "alice" },
        };

        string json = JsonSerializer.Serialize(value);
        using JsonDocument document = JsonDocument.Parse(json);

        string? identity = document.RootElement.GetProperty("$type").GetString();
        Assert.Equal("serialization/aliased-state", identity);
    }

    [Fact]
    public void Serialization_falls_back_to_clr_type_name_when_alias_is_absent()
    {
        var value = new Storage<NonAliasedState>
        {
            Value = new NonAliasedState { Name = "alice" },
        };

        string json = JsonSerializer.Serialize(value);
        using JsonDocument document = JsonDocument.Parse(json);

        string? identity = document.RootElement.GetProperty("$type").GetString();
        Assert.Equal(typeof(NonAliasedState).FullName, identity);
    }

    [Fact]
    public void Roundtrip_for_current_type_sets_migrated_flag_to_false()
    {
        var value = new Storage<AliasedState>
        {
            Value = new AliasedState { Name = "alice" },
        };

        string json = JsonSerializer.Serialize(value);
        Storage<AliasedState>? roundtrip = JsonSerializer.Deserialize<Storage<AliasedState>>(json);

        Assert.NotNull(roundtrip);
        Assert.Equal("alice", roundtrip.Value.Name);
        Assert.False(roundtrip.MigratedDuringDeserialization);
    }
    [global::Orleans.Alias("serialization/aliased-state")]
    public sealed class AliasedState
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class NonAliasedState
    {
        public string Name { get; init; } = string.Empty;
    }
}
