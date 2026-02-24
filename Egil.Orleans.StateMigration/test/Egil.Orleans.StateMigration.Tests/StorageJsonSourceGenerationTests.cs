using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.StateMigration.Tests;

public sealed class StorageJsonSourceGenerationTests
{
    [Fact]
    public void State_only_source_generated_context_supports_storage_serialization()
    {
        JsonSerializerOptions options = CreateOptions();
        var input = new Storage<SourceGenCurrentState>
        {
            Value = new SourceGenCurrentState { DisplayName = "alice" },
        };

        string json = JsonSerializer.Serialize(input, options);
        Storage<SourceGenCurrentState>? result = JsonSerializer.Deserialize<Storage<SourceGenCurrentState>>(json, options);

        Assert.NotNull(result);
        Assert.Equal("alice", result.Value.DisplayName);
        Assert.False(result.MigratedDuringDeserialization);
    }

    [Fact]
    public void State_only_source_generated_context_supports_storage_deserialization_migration()
    {
        JsonSerializerOptions options = CreateOptions();
        string json = """
            {"$type":"sourcegen/legacy-state","Name":"alice"}
            """;

        Storage<SourceGenCurrentState>? result = JsonSerializer.Deserialize<Storage<SourceGenCurrentState>>(json, options);

        Assert.NotNull(result);
        Assert.Equal("migrated:alice", result.Value.DisplayName);
        Assert.True(result.MigratedDuringDeserialization);
    }

    [Fact]
    public void State_only_source_generated_context_supports_current_collection_payload_without_type_metadata()
    {
        JsonSerializerOptions options = CreateOptions();
        string json = """
            ["alice","bob"]
            """;

        Storage<List<string>>? result = JsonSerializer.Deserialize<Storage<List<string>>>(json, options);

        Assert.NotNull(result);
        Assert.Equal(["alice", "bob"], result.Value);
        Assert.True(result.MigratedDuringDeserialization);
    }

    [Fact]
    public void State_only_source_generated_context_supports_custom_type_property_name()
    {
        JsonSerializerOptions options = CreateOptions("_type");
        string json = """
            {"_type":"sourcegen/legacy-state","Name":"alice"}
            """;

        Storage<SourceGenCurrentState>? result = JsonSerializer.Deserialize<Storage<SourceGenCurrentState>>(json, options);

        Assert.NotNull(result);
        Assert.Equal("migrated:alice", result.Value.DisplayName);
        Assert.True(result.MigratedDuringDeserialization);
    }

    private static JsonSerializerOptions CreateOptions()
        => CreateOptions(typePropertyName: null);

    private static JsonSerializerOptions CreateOptions(string? typePropertyName)
    {
        JsonSerializerOptions options = new JsonSerializerOptions(StorageJsonSourceGenerationContext.Default.Options);
        if (typePropertyName is null)
        {
            return options.AddStateMigrationSupport();
        }

        return options.AddStateMigrationSupport(typePropertyName);
    }
}

[Alias("sourcegen/legacy-state")]
public sealed class SourceGenLegacyState
{
    public string Name { get; init; } = string.Empty;
}

[Alias("sourcegen/current-state")]
public sealed class SourceGenCurrentState : IMigrateFrom<SourceGenLegacyState, SourceGenCurrentState>
{
    public string DisplayName { get; init; } = string.Empty;

    public static SourceGenCurrentState From(SourceGenLegacyState source)
        => new() { DisplayName = $"migrated:{source.Name}" };
}

[JsonSerializable(typeof(SourceGenLegacyState))]
[JsonSerializable(typeof(SourceGenCurrentState))]
[JsonSerializable(typeof(List<string>))]
internal partial class StorageJsonSourceGenerationContext : JsonSerializerContext;
