using System.Collections.Concurrent;
using System.Text.Json;
using Orleans.Storage;

namespace Egil.Orleans.StateMigration.Tests;

[Collection(OrleansInProcessClusterCollection.Name)]
public sealed class StorageJsonOrleansInProcessTests(OrleansInProcessClusterFixture fixture)
{
    [Fact]
    public async Task Enveloped_storage_roundtrips_in_orleans_in_process_cluster()
    {
        string displayName = CreateToken("alice");
        string grainKey = CreateToken("user/default");
        IDefaultStorageStateGrain grain = fixture.Cluster.Client.GetGrain<IDefaultStorageStateGrain>(grainKey);

        await grain.SetDisplayNameAsync(displayName);
        await ForceReactivationAsync(grain);

        Assert.Equal(displayName, await grain.GetDisplayNameAsync());
        Assert.False(await grain.WasMigratedOnActivationAsync());

        string json = fixture.DefaultSerializer.GetLatestSerializedJsonContaining(displayName);
        Assert.Contains(@"""$type"":""e2e/current-profile""", json, StringComparison.Ordinal);
        Assert.Contains(@"""$value"":{", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Custom_type_and_value_property_names_are_used_in_cluster_storage()
    {
        string displayName = CreateToken("bob");
        string grainKey = CreateToken("user/custom");
        ICustomStorageStateGrain grain = fixture.Cluster.Client.GetGrain<ICustomStorageStateGrain>(grainKey);

        await grain.SetDisplayNameAsync(displayName);
        Assert.Equal(displayName, await grain.GetDisplayNameAsync());

        string json = fixture.CustomSerializer.GetLatestSerializedJsonContaining(displayName);
        Assert.Contains(@"""_type"":""e2e/current-profile""", json, StringComparison.Ordinal);
        Assert.Contains(@"""_value"":{", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legacy_flattened_payload_is_migrated_and_rewritten_to_enveloped_layout()
    {
        string displayName = CreateToken("charlie");
        string grainKey = CreateToken("user/legacy");
        fixture.DefaultSerializer.RegisterOneShotFlattenedWriteForDisplayName(displayName);

        IDefaultStorageStateGrain grain = fixture.Cluster.Client.GetGrain<IDefaultStorageStateGrain>(grainKey);
        await grain.SetDisplayNameAsync(displayName);

        string flattenedJson = fixture.DefaultSerializer.GetLatestSerializedJsonContaining(displayName);
        Assert.Contains(@"""$type"":""e2e/current-profile""", flattenedJson, StringComparison.Ordinal);
        Assert.DoesNotContain(@"""$value"":", flattenedJson, StringComparison.Ordinal);

        await ForceReactivationAsync(grain);

        Assert.Equal(displayName, await grain.GetDisplayNameAsync());
        Assert.True(await grain.WasMigratedOnActivationAsync());

        string rewrittenJson = fixture.DefaultSerializer.GetLatestSerializedJsonContaining(displayName);
        Assert.Contains(@"""$type"":""e2e/current-profile""", rewrittenJson, StringComparison.Ordinal);
        Assert.Contains(@"""$value"":", rewrittenJson, StringComparison.Ordinal);
    }

    private static async Task ForceReactivationAsync(IDefaultStorageStateGrain grain)
    {
        await grain.Cast<global::Orleans.Core.Internal.IGrainManagementExtension>().DeactivateOnIdle();
        _ = await grain.GetDisplayNameAsync();
    }

    private static string CreateToken(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}";
}

internal interface IDefaultStorageStateGrain : IGrainWithStringKey
{
    Task SetDisplayNameAsync(string displayName);

    Task<string> GetDisplayNameAsync();

    Task<bool> WasMigratedOnActivationAsync();
}

internal interface ICustomStorageStateGrain : IGrainWithStringKey
{
    Task SetDisplayNameAsync(string displayName);

    Task<string> GetDisplayNameAsync();
}

internal sealed class DefaultStorageStateGrain(
    [PersistentState("state", "storage-default")] IPersistentState<Storage<CurrentProfileState>> state)
    : Grain, IDefaultStorageStateGrain
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
}

internal sealed class CustomStorageStateGrain(
    [PersistentState("state", "storage-custom")] IPersistentState<Storage<CurrentProfileState>> state)
    : Grain, ICustomStorageStateGrain
{
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

public sealed class SystemTextJsonGrainStorageSerializer(
    JsonSerializerOptions options,
    JsonSerializerOptions? flattenedWriteOptions) : IGrainStorageSerializer
{
    private readonly JsonSerializerOptions jsonSerializerOptions = options;
    private readonly JsonSerializerOptions? flattenedSerializerOptions = flattenedWriteOptions;
    private readonly ConcurrentDictionary<string, byte> oneShotFlattenedWrites = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> serializedPayloads = new();

    public void RegisterOneShotFlattenedWriteForDisplayName(string displayName)
    {
        if (flattenedSerializerOptions is null)
        {
            throw new InvalidOperationException("Flattened one-shot writes are not supported by this serializer.");
        }

        oneShotFlattenedWrites[displayName] = 0;
    }

    public string GetLatestSerializedJsonContaining(string valueFragment)
    {
        string[] snapshot = serializedPayloads.ToArray();
        for (int i = snapshot.Length - 1; i >= 0; i--)
        {
            if (snapshot[i].Contains(valueFragment, StringComparison.Ordinal))
            {
                return snapshot[i];
            }
        }

        throw new InvalidOperationException(
            $"No serialized payload captured containing fragment '{valueFragment}'.");
    }

    public BinaryData Serialize<T>(T value)
    {
        string json = ShouldSerializeFlattened(value, out Storage<CurrentProfileState>? stateToFlatten)
            ? JsonSerializer.Serialize(stateToFlatten, flattenedSerializerOptions)
            : JsonSerializer.Serialize(value, jsonSerializerOptions);

        serializedPayloads.Enqueue(json);
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

    private bool ShouldSerializeFlattened<T>(T value, [NotNullWhen(true)] out Storage<CurrentProfileState>? stateToFlatten)
    {
        stateToFlatten = value as Storage<CurrentProfileState>;
        if (stateToFlatten is null || flattenedSerializerOptions is null)
        {
            return false;
        }

        return oneShotFlattenedWrites.TryRemove(stateToFlatten.Value.DisplayName, out _);
    }
}
