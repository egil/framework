using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging.State.Consumer.Tests.State;

public sealed class VersionedStateSourceGenerationTests
{
    [Fact]
    public void Generated_context_round_trips_version_and_state_properties()
    {
        var original = new SourceGeneratedState { Name = "order", Count = 3 };

        var json = JsonSerializer.Serialize(original, StateJsonContext.Default.SourceGeneratedState);
        var restored = JsonSerializer.Deserialize(json, StateJsonContext.Default.SourceGeneratedState);

        Assert.NotNull(restored);
        Assert.Equal(original.Version, restored.Version);
        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Count, restored.Count);
    }

    [Fact]
    public void Previously_stored_version_json_reads_with_generated_and_reflection_serializers()
    {
        const string json = """{"Version":"01976841-b9f8-7d68-9c78-9489b9f26e54","Name":"order","Count":3}""";

        var generated = JsonSerializer.Deserialize(json, StateJsonContext.Default.SourceGeneratedState);
        var reflected = JsonSerializer.Deserialize<SourceGeneratedState>(json);

        Assert.NotNull(generated);
        Assert.NotNull(reflected);
        Assert.Equal(Guid.Parse("01976841-b9f8-7d68-9c78-9489b9f26e54"), generated.Version);
        Assert.Equal(generated, reflected);
    }

    [Fact]
    public async Task State_manager_stamps_and_restores_version_through_generated_context()
    {
        var storage = new JsonPersistentState();
        var manager = new DefaultStateManager<SourceGeneratedState>(storage, static () => new());
        var candidate = new SourceGeneratedState { Name = "order", Count = 3 };
        var originalVersion = candidate.Version;

        await manager.WriteAsync(candidate, TestContext.Current.CancellationToken);
        var writtenVersion = manager.State.Version;
        Assert.NotSame(candidate, manager.State);
        await manager.ReadAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(originalVersion, writtenVersion);
        Assert.Equal(originalVersion, candidate.Version);
        Assert.Equal(writtenVersion, manager.State.Version);
        Assert.Equal("order", manager.State.Name);
        Assert.Equal(3, manager.State.Count);
    }

    private sealed class JsonPersistentState : IPersistentState<SourceGeneratedState>
    {
        private string? json;

        public SourceGeneratedState State { get; set; } = new();
        public string Etag { get; set; } = "etag";
        public bool RecordExists { get; set; }

        public Task ReadStateAsync() => ReadStateAsync(CancellationToken.None);
        public Task WriteStateAsync() => WriteStateAsync(CancellationToken.None);
        public Task ClearStateAsync() => ClearStateAsync(CancellationToken.None);

        public Task ReadStateAsync(CancellationToken cancellationToken)
        {
            State = json is null
                ? new()
                : JsonSerializer.Deserialize(json, StateJsonContext.Default.SourceGeneratedState)!;
            RecordExists = json is not null;
            return Task.CompletedTask;
        }

        public Task WriteStateAsync(CancellationToken cancellationToken)
        {
            json = JsonSerializer.Serialize(State, StateJsonContext.Default.SourceGeneratedState);
            RecordExists = true;
            return Task.CompletedTask;
        }

        public Task ClearStateAsync(CancellationToken cancellationToken)
        {
            json = null;
            RecordExists = false;
            return Task.CompletedTask;
        }
    }
}

public sealed record SourceGeneratedState : VersionedState
{
    public string Name { get; init; } = "";
    public int Count { get; init; }
}

[JsonSerializable(typeof(SourceGeneratedState))]
public partial class StateJsonContext : JsonSerializerContext;
