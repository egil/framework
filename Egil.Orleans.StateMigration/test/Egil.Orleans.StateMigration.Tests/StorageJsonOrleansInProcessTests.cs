using System.Text.Json;
using Orleans.Storage;
using Orleans.TestingHost;

namespace Egil.Orleans.StateMigration.Tests;

public sealed class StorageJsonOrleansInProcessTests
{
    [Fact]
    public async Task Enveloped_storage_roundtrips_in_orleans_in_process_cluster()
    {
        JsonSerializerOptions jsonOptions = new JsonSerializerOptions().AddStateMigrationSupport();
        var serializer = new SystemTextJsonGrainStorageSerializer(jsonOptions);
        await using InProcessTestCluster cluster = await StartClusterAsync(serializer);

        IStorageStateGrain grain = cluster.Client.GetGrain<IStorageStateGrain>("user/default");
        await grain.SetDisplayNameAsync("alice");
        await grain.DeactivateAsync();

        string displayName = await grain.GetDisplayNameAsync();
        bool migratedOnActivation = await grain.WasMigratedOnActivationAsync();

        Assert.Equal("alice", displayName);
        Assert.False(migratedOnActivation);
        Assert.Contains(@"""$type"":""e2e/current-profile""", serializer.LastSerializedJson, StringComparison.Ordinal);
        Assert.Contains(@"""$value"":{", serializer.LastSerializedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Custom_type_and_value_property_names_are_used_in_cluster_storage()
    {
        JsonSerializerOptions jsonOptions = new JsonSerializerOptions()
            .AddStateMigrationSupport("_type", "_value");
        var serializer = new SystemTextJsonGrainStorageSerializer(jsonOptions);
        await using InProcessTestCluster cluster = await StartClusterAsync(serializer);

        IStorageStateGrain grain = cluster.Client.GetGrain<IStorageStateGrain>("user/custom");
        await grain.SetDisplayNameAsync("bob");

        Assert.Contains(@"""_type"":""e2e/current-profile""", serializer.LastSerializedJson, StringComparison.Ordinal);
        Assert.Contains(@"""_value"":{", serializer.LastSerializedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legacy_flattened_payload_is_migrated_and_rewritten_to_enveloped_layout()
    {
        JsonSerializerOptions readAndRewriteOptions = new JsonSerializerOptions().AddStateMigrationSupport();
        var flattenedWriteOptions = new JsonSerializerOptions().AddStateMigrationSupport(StoragePayloadLayout.Flattened);
        var serializer = new SystemTextJsonGrainStorageSerializer(readAndRewriteOptions);
        serializer.SetNextSerializeOverride(payload =>
        {
            if (payload is not Storage<CurrentProfileState> storage)
            {
                throw new InvalidOperationException(
                    $"Expected payload of type '{typeof(Storage<CurrentProfileState>)}'.");
            }

            return JsonSerializer.Serialize(storage, flattenedWriteOptions);
        });

        await using InProcessTestCluster cluster = await StartClusterAsync(serializer);
        IStorageStateGrain grain = cluster.Client.GetGrain<IStorageStateGrain>("user/legacy");

        await grain.SetDisplayNameAsync("charlie");
        Assert.Contains(@"""$type"":""e2e/current-profile""", serializer.LastSerializedJson, StringComparison.Ordinal);
        Assert.DoesNotContain(@"""$value"":", serializer.LastSerializedJson, StringComparison.Ordinal);

        await grain.DeactivateAsync();
        string displayNameAfterReactivation = await grain.GetDisplayNameAsync();
        bool migratedOnActivation = await grain.WasMigratedOnActivationAsync();

        Assert.Equal("charlie", displayNameAfterReactivation);
        Assert.True(migratedOnActivation);
        Assert.Contains(@"""$type"":""e2e/current-profile""", serializer.LastSerializedJson, StringComparison.Ordinal);
        Assert.Contains(@"""$value"":", serializer.LastSerializedJson, StringComparison.Ordinal);
    }

    private static async Task<InProcessTestCluster> StartClusterAsync(SystemTextJsonGrainStorageSerializer serializer)
    {
        var builder = new InProcessTestClusterBuilder();
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorage("storage", optionsBuilder =>
            {
                optionsBuilder.Configure(options =>
                {
                    options.GrainStorageSerializer = serializer;
                });
            });
        });

        InProcessTestCluster cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }
}

internal interface IStorageStateGrain : IGrainWithStringKey
{
    Task SetDisplayNameAsync(string displayName);

    Task<string> GetDisplayNameAsync();

    Task<bool> WasMigratedOnActivationAsync();

    Task DeactivateAsync();
}

internal sealed class StorageStateGrain(
    [PersistentState("state", "storage")] IPersistentState<Storage<CurrentProfileState>> state)
    : Grain, IStorageStateGrain
{
    private bool migratedOnActivation;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        migratedOnActivation = state.State?.MigratedDuringDeserialization ?? false;
        if (migratedOnActivation)
        {
            await state.WriteStateAsync();
        }
    }

    public async Task SetDisplayNameAsync(string displayName)
    {
        state.State = new Storage<CurrentProfileState>
        {
            Value = new CurrentProfileState { DisplayName = displayName },
        };

        await state.WriteStateAsync();
    }

    public Task<string> GetDisplayNameAsync()
        => Task.FromResult(state.State?.Value.DisplayName ?? string.Empty);

    public Task<bool> WasMigratedOnActivationAsync()
        => Task.FromResult(migratedOnActivation);

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}

[Alias("e2e/legacy-profile")]
internal sealed class LegacyProfileState
{
    public string Name { get; init; } = string.Empty;
}

[Alias("e2e/current-profile")]
internal sealed class CurrentProfileState : IMigrateFrom<LegacyProfileState, CurrentProfileState>
{
    public string DisplayName { get; init; } = string.Empty;

    public static CurrentProfileState From(LegacyProfileState source)
        => new() { DisplayName = $"migrated:{source.Name}" };
}

internal sealed class SystemTextJsonGrainStorageSerializer(JsonSerializerOptions options) : IGrainStorageSerializer
{
    private readonly JsonSerializerOptions jsonSerializerOptions = options;
    private Func<object?, string>? nextSerializeOverride;

    public string LastSerializedJson { get; private set; } = string.Empty;

    public void SetNextSerializeOverride(Func<object?, string> serializeOverride)
        => nextSerializeOverride = serializeOverride;

    public BinaryData Serialize<T>(T value)
    {
        Func<object?, string>? serializeOverride = Interlocked.Exchange(ref nextSerializeOverride, null);
        string json = serializeOverride is null
            ? JsonSerializer.Serialize(value, jsonSerializerOptions)
            : serializeOverride(value);

        LastSerializedJson = json;
        return BinaryData.FromString(json);
    }

    public T Deserialize<T>(BinaryData input)
    {
        T? value = JsonSerializer.Deserialize<T>(input.ToString(), jsonSerializerOptions);
        if (value is null && default(T) is not null)
        {
            throw new InvalidOperationException(
                $"Could not deserialize non-nullable storage payload for '{typeof(T)}'.");
        }

        return value!;
    }
}
