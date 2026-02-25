using System.Text;
using System.Text.Json;

namespace Egil.Orleans.StateMigration.Tests.SystemTextJson;

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
    public void Serialization_wraps_state_in_value_property()
    {
        var value = new Storage<NonAliasedState>
        {
            Value = new NonAliasedState { Name = "alice" },
        };

        string json = JsonSerializer.Serialize(value);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("$value", out JsonElement stateElement));
        Assert.Equal("alice", stateElement.GetProperty("Name").GetString());
    }

    [Fact]
    public void Serialization_supports_non_object_state_shapes()
    {
        var value = new Storage<List<string>>
        {
            Value = ["alice", "bob"],
        };

        string json = JsonSerializer.Serialize(value);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("$value", out JsonElement stateElement));
        Assert.Equal(JsonValueKind.Array, stateElement.ValueKind);
        Assert.Equal("alice", stateElement[0].GetString());
        Assert.Equal("bob", stateElement[1].GetString());
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

    [Fact]
    public void Serialization_uses_configured_type_property_name()
    {
        JsonSerializerOptions options = new JsonSerializerOptions().AddStateMigrationSupport("_type");
        var value = new Storage<AliasedState>
        {
            Value = new AliasedState { Name = "alice" },
        };

        string json = JsonSerializer.Serialize(value, options);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("_type", out JsonElement typeProperty));
        Assert.Equal("serialization/aliased-state", typeProperty.GetString());
        Assert.True(document.RootElement.TryGetProperty("$value", out _));
    }

    [Fact]
    public void Serialization_uses_configured_value_property_name()
    {
        JsonSerializerOptions options = new JsonSerializerOptions().AddStateMigrationSupport("$type", "_value");
        var value = new Storage<AliasedState>
        {
            Value = new AliasedState { Name = "alice" },
        };

        string json = JsonSerializer.Serialize(value, options);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("_value", out JsonElement stateElement));
        Assert.Equal("alice", stateElement.GetProperty("Name").GetString());
        Assert.False(document.RootElement.TryGetProperty("$value", out _));
    }

    [Fact]
    public void Serialization_can_write_flattened_payload_when_configured()
    {
        JsonSerializerOptions options = new JsonSerializerOptions()
            .AddStateMigrationSupport(payloadLayout: StoragePayloadLayout.Flattened);
        var value = new Storage<AliasedState>
        {
            Value = new AliasedState { Name = "alice" },
        };

        string json = JsonSerializer.Serialize(value, options);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal("serialization/aliased-state", document.RootElement.GetProperty("$type").GetString());
        Assert.Equal("alice", document.RootElement.GetProperty("Name").GetString());
        Assert.False(document.RootElement.TryGetProperty("$value", out _));
    }

    [Fact]
    public void Flattened_serialization_rejects_non_object_state_shapes()
    {
        JsonSerializerOptions options = new JsonSerializerOptions()
            .AddStateMigrationSupport(payloadLayout: StoragePayloadLayout.Flattened);
        var value = new Storage<List<string>>
        {
            Value = ["alice", "bob"],
        };

        JsonException exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Serialize(value, options));

        Assert.Contains("Flattened storage payload requires", exception.Message, StringComparison.Ordinal);
    }

    [Alias("serialization/aliased-state")]
    public sealed class AliasedState
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class NonAliasedState
    {
        public string Name { get; init; } = string.Empty;
    }
}
