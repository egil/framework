using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.StateMigration.Tests;

public sealed class StorageJsonConverterOnDeserializedTests
{
    [Fact]
    public void On_deserialized_callback_is_invoked_for_current_type_path()
    {
        string json = """
            {"$type":"on-deserialized/current-state","Name":"alice"}
            """;

        Storage<CurrentState>? result = JsonSerializer.Deserialize<Storage<CurrentState>>(json);

        Assert.NotNull(result);
        Assert.Equal("alice", result.Value.Name);
        Assert.Equal(1, result.Value.OnDeserializedCount);
        Assert.False(result.MigratedDuringDeserialization);
    }

    [Fact]
    public void On_deserialized_callback_is_invoked_for_migration_path()
    {
        string json = """
            {"$type":"on-deserialized/legacy-state","Name":"alice"}
            """;

        Storage<CurrentState>? result = JsonSerializer.Deserialize<Storage<CurrentState>>(json);

        Assert.NotNull(result);
        Assert.Equal("migrated:alice", result.Value.Name);
        Assert.Equal(1, result.Value.OnDeserializedCount);
        Assert.True(result.MigratedDuringDeserialization);
    }

    [Fact]
    public void On_deserialized_callback_is_invoked_only_once_per_deserialization()
    {
        string json = """
            {"$type":"on-deserialized/current-state","Name":"alice"}
            """;

        Storage<CurrentState>? result = JsonSerializer.Deserialize<Storage<CurrentState>>(json);

        Assert.NotNull(result);
        Assert.Equal(1, result.Value.OnDeserializedCount);
    }
    [global::Orleans.Alias("on-deserialized/legacy-state")]
    public sealed class LegacyState
    {
        public string Name { get; init; } = string.Empty;
    }

    [global::Orleans.Alias("on-deserialized/current-state")]
    public sealed class CurrentState :
        IMigrateFrom<LegacyState, CurrentState>,
        global::Orleans.Serialization.IOnDeserialized
    {
        public string Name { get; init; } = string.Empty;

        [JsonIgnore]
        public int OnDeserializedCount { get; private set; }

        public static CurrentState From(LegacyState source)
            => new() { Name = $"migrated:{source.Name}" };

        public void OnDeserialized(global::Orleans.Serialization.DeserializationContext context)
            => OnDeserializedCount++;
    }
}
