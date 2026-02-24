using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.StateMigration.Tests;

public sealed class StorageJsonConverterPhase4Tests
{
    [Fact]
    public void On_deserialized_callback_is_invoked_for_current_type_path()
    {
        string json = """
            {"$type":"phase4/current-state","Name":"alice"}
            """;

        Storage<Phase4CurrentState>? result = JsonSerializer.Deserialize<Storage<Phase4CurrentState>>(json);

        Assert.NotNull(result);
        Assert.Equal("alice", result.State.Name);
        Assert.Equal(1, result.State.OnDeserializedCount);
        Assert.False(result.MigratedDuringDeserialization);
    }

    [Fact]
    public void On_deserialized_callback_is_invoked_for_migration_path()
    {
        string json = """
            {"$type":"phase4/legacy-state","Name":"alice"}
            """;

        Storage<Phase4CurrentState>? result = JsonSerializer.Deserialize<Storage<Phase4CurrentState>>(json);

        Assert.NotNull(result);
        Assert.Equal("migrated:alice", result.State.Name);
        Assert.Equal(1, result.State.OnDeserializedCount);
        Assert.True(result.MigratedDuringDeserialization);
    }

    [Fact]
    public void On_deserialized_callback_is_invoked_only_once_per_deserialization()
    {
        string json = """
            {"$type":"phase4/current-state","Name":"alice"}
            """;

        Storage<Phase4CurrentState>? result = JsonSerializer.Deserialize<Storage<Phase4CurrentState>>(json);

        Assert.NotNull(result);
        Assert.Equal(1, result.State.OnDeserializedCount);
    }
}

[global::Orleans.Alias("phase4/legacy-state")]
public sealed class Phase4LegacyState
{
    public string Name { get; init; } = string.Empty;
}

[global::Orleans.Alias("phase4/current-state")]
public sealed class Phase4CurrentState :
    IMigrateFrom<Phase4LegacyState, Phase4CurrentState>,
    global::Orleans.Serialization.IOnDeserialized
{
    public string Name { get; init; } = string.Empty;

    [JsonIgnore]
    public int OnDeserializedCount { get; private set; }

    public static Phase4CurrentState From(Phase4LegacyState source)
        => new() { Name = $"migrated:{source.Name}" };

    public void OnDeserialized(global::Orleans.Serialization.DeserializationContext context)
        => OnDeserializedCount++;
}
