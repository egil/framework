using System.Text.Json;

namespace Egil.Orleans.StateMigration.Tests;

public sealed class StorageJsonConverterDeserializationTests
{
    [Fact]
    public void Matching_type_metadata_uses_current_type_fast_path()
    {
        string json = """
            {"$type":"deserialization/current-state","DisplayName":"alice"}
            """;

        Storage<CurrentState>? result = JsonSerializer.Deserialize<Storage<CurrentState>>(json);

        Assert.NotNull(result);
        Assert.Equal("alice", result.Value.DisplayName);
        Assert.False(result.MigratedDuringDeserialization);
    }

    [Fact]
    public void Known_older_type_triggers_migration_and_marks_payload_as_migrated()
    {
        string json = """
            {"$type":"deserialization/legacy-state","Name":"alice"}
            """;

        Storage<CurrentState>? result = JsonSerializer.Deserialize<Storage<CurrentState>>(json);

        Assert.NotNull(result);
        Assert.Equal("migrated:alice", result.Value.DisplayName);
        Assert.True(result.MigratedDuringDeserialization);
    }

    [Theory]
    [InlineData("""{"$type":null,"Name":"alice"}""", "$type cannot be null or empty")]
    [InlineData("""{"$type":"","Name":"alice"}""", "$type cannot be null or empty")]
    [InlineData("""{"$type":"deserialization/unknown-state","Name":"alice"}""", "is unknown")]
    public void Malformed_or_unknown_type_metadata_fails_fast_with_clear_exception(string json, string messageFragment)
    {
        JsonException exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<Storage<CurrentState>>(json));

        Assert.Contains(messageFragment, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_payload_without_type_metadata_deserializes_as_current()
    {
        string json = """
            {"DisplayName":"alice"}
            """;

        Storage<CurrentState>? result = JsonSerializer.Deserialize<Storage<CurrentState>>(json);

        Assert.NotNull(result);
        Assert.Equal("alice", result.Value.DisplayName);
        Assert.True(result.MigratedDuringDeserialization);
    }

    [Fact]
    public void Legacy_payload_without_type_metadata_deserializes_as_current_and_marks_migrated()
    {
        string json = """
            {"Name":"alice"}
            """;

        Storage<CurrentState>? result = JsonSerializer.Deserialize<Storage<CurrentState>>(json);

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Value.DisplayName);
        Assert.True(result.MigratedDuringDeserialization);
    }
    [global::Orleans.Alias("deserialization/legacy-state")]
    public sealed class LegacyState
    {
        public string Name { get; init; } = string.Empty;
    }

    [global::Orleans.Alias("deserialization/current-state")]
    public sealed class CurrentState : IMigrateFrom<LegacyState, CurrentState>
    {
        public string DisplayName { get; init; } = string.Empty;

        public static CurrentState From(LegacyState source)
            => new() { DisplayName = $"migrated:{source.Name}" };
    }
}
