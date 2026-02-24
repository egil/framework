using System.Text.Json;

namespace Egil.Orleans.StateMigration.Tests;

public sealed class StorageJsonConverterPhase3Tests
{
    [Fact]
    public void Matching_type_metadata_uses_current_type_fast_path()
    {
        string json = """
            {"$type":"phase3/current-state","DisplayName":"alice"}
            """;

        Storage<Phase3CurrentState>? result = JsonSerializer.Deserialize<Storage<Phase3CurrentState>>(json);

        Assert.NotNull(result);
        Assert.Equal("alice", result.State.DisplayName);
        Assert.False(result.MigratedDuringDeserialization);
    }

    [Fact]
    public void Known_older_type_triggers_migration_and_marks_payload_as_migrated()
    {
        string json = """
            {"$type":"phase3/legacy-state","Name":"alice"}
            """;

        Storage<Phase3CurrentState>? result = JsonSerializer.Deserialize<Storage<Phase3CurrentState>>(json);

        Assert.NotNull(result);
        Assert.Equal("migrated:alice", result.State.DisplayName);
        Assert.True(result.MigratedDuringDeserialization);
    }

    [Theory]
    [InlineData("""{"$type":null,"Name":"alice"}""", "$type cannot be null or empty")]
    [InlineData("""{"$type":"","Name":"alice"}""", "$type cannot be null or empty")]
    [InlineData("""{"$type":"phase3/unknown-state","Name":"alice"}""", "is unknown")]
    public void Malformed_or_unknown_type_metadata_fails_fast_with_clear_exception(string json, string messageFragment)
    {
        JsonException exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<Storage<Phase3CurrentState>>(json));

        Assert.Contains(messageFragment, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_payload_without_type_metadata_deserializes_as_current_and_marks_migrated()
    {
        string json = """
            {"DisplayName":"alice"}
            """;

        Storage<Phase3CurrentState>? result = JsonSerializer.Deserialize<Storage<Phase3CurrentState>>(json);

        Assert.NotNull(result);
        Assert.Equal("alice", result.State.DisplayName);
        Assert.True(result.MigratedDuringDeserialization);
    }
}

[global::Orleans.Alias("phase3/legacy-state")]
public sealed class Phase3LegacyState
{
    public string Name { get; init; } = string.Empty;
}

[global::Orleans.Alias("phase3/current-state")]
public sealed class Phase3CurrentState : IMigrateFrom<Phase3LegacyState, Phase3CurrentState>
{
    public string DisplayName { get; init; } = string.Empty;

    public static Phase3CurrentState From(Phase3LegacyState source)
        => new() { DisplayName = $"migrated:{source.Name}" };
}
