namespace Egil.Orleans.Messaging.Tests.State;

/// <summary>
/// What a deferred write does across a live grain migration. The library takes no part in
/// Orleans' migration handoff, so the outcome is decided by whether the grain persists
/// on the way out — which is what these pin, in both directions.
/// </summary>
public sealed class StateManagerMigrationTests(MigrationTestClusterFixture fixture)
    : IClassFixture<MigrationTestClusterFixture>
{
    [Fact]
    public async Task A_grain_that_saves_on_deactivation_carries_unsaved_changes_across_a_migration()
    {
        var grain = fixture.GrainFactory.GetGrain<IFlushingMigratingStateGrain>(Guid.NewGuid());
        await grain.SaveAsync("stored");
        await grain.ChangeAsync("staged");
        var origin = (await grain.ObserveAsync()).SiloAddress;

        await fixture.Cluster.MigrateAsync(grain, fixture.OtherSiloThan(origin));

        // Orleans runs OnDeactivateAsync before it dehydrates, so the write lands first
        // and the destination inherits a value storage genuinely holds.
        var migrated = await grain.ObserveAsync();
        Assert.NotEqual(origin, migrated.SiloAddress);
        Assert.Equal("staged", migrated.Value);
        Assert.False(migrated.HasUnsavedChanges);

        var reread = await grain.RereadAsync();
        Assert.Equal("staged", reread.Value);
        Assert.True(reread.RecordExists);
    }

    [Fact]
    public async Task Without_that_save_a_migrated_unsaved_value_looks_durable_but_is_not()
    {
        var grain = fixture.GrainFactory.GetGrain<IMigratingStateGrain>(Guid.NewGuid());
        await grain.SaveAsync("stored");
        await grain.ChangeAsync("staged");
        var origin = (await grain.ObserveAsync()).SiloAddress;

        await fixture.Cluster.MigrateAsync(grain, fixture.OtherSiloThan(origin));

        // The staged value rides along inside the storage facet, which Orleans carries
        // itself, and the destination has no way to know it was never written. This is
        // the exposure the deactivation hook exists to close.
        var migrated = await grain.ObserveAsync();
        Assert.NotEqual(origin, migrated.SiloAddress);
        Assert.Equal("staged", migrated.Value);
        Assert.False(migrated.HasUnsavedChanges);

        var reread = await grain.RereadAsync();
        Assert.Equal("stored", reread.Value);
    }

    [Fact]
    public async Task Without_that_save_a_change_made_before_the_first_write_does_not_survive()
    {
        var grain = fixture.GrainFactory.GetGrain<IMigratingStateGrain>(Guid.NewGuid());
        await grain.ChangeAsync("staged");
        var origin = (await grain.ObserveAsync()).SiloAddress;

        await fixture.Cluster.MigrateAsync(grain, fixture.OtherSiloThan(origin));

        // No record exists yet, so the facet arrives with RecordExists false and the
        // destination resolves its configured default instead. The staged value is gone
        // on arrival rather than at the next deactivation.
        var migrated = await grain.ObserveAsync();
        Assert.NotEqual(origin, migrated.SiloAddress);
        Assert.Equal("default", migrated.Value);
        Assert.False(migrated.HasUnsavedChanges);
    }
}

public interface IMigratingStateGrain : IGrainWithGuidKey
{
    Task SaveAsync(string value);
    Task ChangeAsync(string value);
    Task<MigrationObservation> ObserveAsync();
    Task<MigrationObservation> RereadAsync();
}

public interface IFlushingMigratingStateGrain : IGrainWithGuidKey
{
    Task SaveAsync(string value);
    Task ChangeAsync(string value);
    Task<MigrationObservation> ObserveAsync();
    Task<MigrationObservation> RereadAsync();
}

[GenerateSerializer]
public sealed record MigrationObservation(
    [property: Id(0)] string Value,
    [property: Id(1)] bool HasUnsavedChanges,
    [property: Id(2)] string SiloAddress,
    [property: Id(3)] bool RecordExists);

[GenerateSerializer]
public sealed record MigratingState
{
    [Id(0)] public string Value { get; init; } = "provider-default";
}

public sealed class MigratingStateGrain : Grain, IMigratingStateGrain
{
    private readonly IPersistentState<MigratingState> storage;
    private readonly IStateManager<MigratingState> manager;
    private readonly ILocalSiloDetails siloDetails;

    public MigratingStateGrain(
        [PersistentState("state", "Default")] IPersistentState<MigratingState> storage,
        ILocalSiloDetails siloDetails)
    {
        this.storage = storage;
        this.siloDetails = siloDetails;
        manager = this.RegisterStateManager("Default", storage, static () => new() { Value = "default" });
    }

    public Task SaveAsync(string value) => manager.WriteAsync(manager.State with { Value = value });

    public Task ChangeAsync(string value)
    {
        manager.State = manager.State with { Value = value };
        return Task.CompletedTask;
    }

    public Task<MigrationObservation> ObserveAsync() => Task.FromResult(CurrentObservation());

    public async Task<MigrationObservation> RereadAsync()
    {
        await manager.ReadAsync();
        return CurrentObservation();
    }

    private MigrationObservation CurrentObservation() => new(
        manager.State.Value,
        manager.HasUnsavedChanges,
        siloDetails.SiloAddress.ToString(),
        storage.RecordExists);
}

public sealed class FlushingMigratingStateGrain : Grain, IFlushingMigratingStateGrain
{
    private readonly IPersistentState<MigratingState> storage;
    private readonly IStateManager<MigratingState> manager;
    private readonly ILocalSiloDetails siloDetails;

    public FlushingMigratingStateGrain(
        [PersistentState("state", "Default")] IPersistentState<MigratingState> storage,
        ILocalSiloDetails siloDetails)
    {
        this.storage = storage;
        this.siloDetails = siloDetails;
        manager = this.RegisterStateManager("Default", storage, static () => new() { Value = "default" });
    }

    public Task SaveAsync(string value) => manager.WriteAsync(manager.State with { Value = value });

    public Task ChangeAsync(string value)
    {
        manager.State = manager.State with { Value = value };
        return Task.CompletedTask;
    }

    public Task<MigrationObservation> ObserveAsync() => Task.FromResult(CurrentObservation());

    public async Task<MigrationObservation> RereadAsync()
    {
        await manager.ReadAsync();
        return CurrentObservation();
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        // The idiom documented on IStateManager<T>.Stage, unconditional: the library does
        // not take part in Orleans' migration handoff, so a migration is just one more
        // deactivation to persist before.
        try
        {
            await manager.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // A real grain logs here; losing the staged value costs a redelivery.
        }

        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    private MigrationObservation CurrentObservation() => new(
        manager.State.Value,
        manager.HasUnsavedChanges,
        siloDetails.SiloAddress.ToString(),
        storage.RecordExists);
}
