using System.Collections.Immutable;
using Orleans.Concurrency;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxReminderPolicyTests(OutboxReminderFixture fixture)
    : IClassFixture<OutboxReminderFixture>
{
    [Fact]
    public async Task Foreground_delivery_establishes_a_reminder_before_dispatch_and_retains_it_after_success()
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();

        await grain.PublishAsync("first", inBackground: false);

        var state = await grain.ObserveAsync();
        Assert.Equal(1, state.Delivered);
        Assert.True(state.ReminderSeenByPostman);
        Assert.NotNull(await grain.GetReminderVersionAsync());
    }

    [Fact]
    public async Task Background_delivery_establishes_a_reminder_before_dispatch_and_retains_it_after_success()
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();

        await grain.PublishAsync("first", inBackground: true);

        Assert.NotNull(await grain.GetReminderVersionAsync());
        await grain.WaitForDeliveryAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var state = await grain.ObserveAsync();
        Assert.Equal(1, state.Delivered);
        Assert.True(state.ReminderSeenByPostman);
        Assert.NotNull(await grain.GetReminderVersionAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_post_establishes_a_reminder_that_survives_empty_ticks(bool inBackground)
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();

        await grain.PostAsync(inBackground);
        var version = await grain.GetReminderVersionAsync();
        Assert.NotNull(version);

        await grain.DeliverReminderAsync();

        Assert.Equal(version, await grain.GetReminderVersionAsync());
        Assert.Equal(0, (await grain.ObserveAsync()).Delivered);
    }

    [Fact]
    public async Task Reminder_is_reused_across_batches_and_reactivation_without_another_write()
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        await grain.PublishAsync("first", inBackground: false);
        var version = await grain.GetReminderVersionAsync();
        var activation = (await grain.ObserveAsync()).Activation;
        Assert.NotNull(version);

        await grain.DeactivateAsync();
        await grain.DeliverReminderAsync();
        await grain.PublishAsync("second", inBackground: false);

        var state = await grain.ObserveAsync();
        Assert.NotEqual(activation, state.Activation);
        Assert.Equal(2, state.Delivered);
        Assert.True(state.ReminderSeenByPostman);
        Assert.Equal(version, await grain.GetReminderVersionAsync());
    }

    [Fact]
    public async Task Retained_reminder_delivers_a_later_batch_persisted_without_posting()
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        await grain.PublishAsync("first", inBackground: false);
        Assert.NotNull(await grain.GetReminderVersionAsync());
        await grain.PersistAsync("second");
        var activation = (await grain.ObserveAsync()).Activation;

        await grain.DeactivateAsync();
        await grain.DeliverReminderAsync();
        await grain.WaitForDeliveryAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var state = await grain.ObserveAsync();
        Assert.NotEqual(activation, state.Activation);
        Assert.Equal(2, state.Delivered);
        Assert.Equal(0, state.Pending);
        Assert.NotNull(await grain.GetReminderVersionAsync());
    }

    [Fact]
    public async Task Default_policy_does_not_create_a_reminder_for_successful_dispatch()
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        await grain.UseDefaultPolicyAsync();
        await grain.DeactivateAsync();

        await grain.PublishAsync("first", inBackground: false);

        Assert.Equal(1, (await grain.ObserveAsync()).Delivered);
        Assert.Null(await grain.GetReminderVersionAsync());
    }

    [Fact]
    public async Task Returning_to_default_policy_removes_an_inherited_reminder_after_draining()
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        await grain.PostAsync(inBackground: false);
        Assert.NotNull(await grain.GetReminderVersionAsync());

        await grain.UseDefaultPolicyAsync();
        await grain.DeactivateAsync();
        await grain.DeliverReminderAsync();

        Assert.Null(await grain.GetReminderVersionAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_reminder_registration_fails_the_post_and_a_later_post_can_recover(bool inBackground)
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        fixture.Reminders.RejectWrites(grain.GetGrainId());

        await Assert.ThrowsAnyAsync<Exception>(() => grain.PublishAsync("pending", inBackground));

        Assert.True(fixture.Reminders.FailedWrites(grain.GetGrainId()) > 0);
        Assert.Null(await grain.GetReminderVersionAsync());
        var failed = await grain.ObserveAsync();
        Assert.Equal(0, failed.Delivered);
        Assert.Equal(1, failed.Pending);

        fixture.Reminders.AllowWrites(grain.GetGrainId());
        await grain.PostAsync(inBackground);
        await grain.WaitForDeliveryAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var recovered = await grain.ObserveAsync();
        Assert.Equal(1, recovered.Delivered);
        Assert.Equal(0, recovered.Pending);
        Assert.NotNull(await grain.GetReminderVersionAsync());
    }

    [Fact]
    public async Task Deactivation_establishes_a_reminder_for_work_persisted_without_posting()
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        await grain.PersistAsync("pending");
        var activation = (await grain.ObserveAsync()).Activation;
        Assert.Null(await grain.GetReminderVersionAsync());

        await grain.DeactivateAsync();

        Assert.NotEqual(activation, (await grain.ObserveAsync()).Activation);
        Assert.NotNull(await grain.GetReminderVersionAsync());
        await grain.DeliverReminderAsync();
        await grain.WaitForDeliveryAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(0, (await grain.ObserveAsync()).Pending);
    }

    [Fact]
    public async Task Deactivation_retries_a_failed_reminder_registration_while_work_remains()
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        await grain.UseDefaultPolicyAsync();
        await grain.DeactivateAsync();
        await grain.RejectDeliveryAsync();
        fixture.Reminders.RejectWrites(grain.GetGrainId());
        await Assert.ThrowsAnyAsync<Exception>(() => grain.PublishAsync("pending", inBackground: false));
        Assert.Null(await grain.GetReminderVersionAsync());
        var activation = (await grain.ObserveAsync()).Activation;
        fixture.Reminders.AllowWrites(grain.GetGrainId());

        await grain.DeactivateAsync();

        Assert.NotEqual(activation, (await grain.ObserveAsync()).Activation);
        Assert.NotNull(await grain.GetReminderVersionAsync());
    }

    [Fact]
    public async Task Failed_deactivation_reminder_logs_the_grain_id_for_manual_recovery()
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        await grain.PersistAsync("pending");
        var activation = (await grain.ObserveAsync()).Activation;
        fixture.Reminders.RejectWrites(grain.GetGrainId());

        await grain.DeactivateAsync();

        var pending = await grain.ObserveAsync();
        Assert.NotEqual(activation, pending.Activation);
        Assert.Equal(1, pending.Pending);
        Assert.Null(await grain.GetReminderVersionAsync());
        var warning = Assert.Single(fixture.Logs.Warnings, entry =>
            entry.EventId.Name == "OutboxDeactivationReminderFailed"
            && Equals(entry.Properties.GetValueOrDefault("GrainId"), grain.GetGrainId()));
        Assert.NotNull(warning.Exception);
        Assert.Contains("manual reactivation", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(warning.Properties["ReminderName"]);

        fixture.Reminders.AllowWrites(grain.GetGrainId());
        await grain.PostAsync(inBackground: false);
        Assert.Equal(0, (await grain.ObserveAsync()).Pending);
    }

    [Fact]
    public async Task Empty_deactivation_does_not_create_a_reminder_or_warn()
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        var activation = (await grain.ObserveAsync()).Activation;
        fixture.Reminders.RejectWrites(grain.GetGrainId());

        await grain.DeactivateAsync();

        Assert.NotEqual(activation, (await grain.ObserveAsync()).Activation);
        Assert.Null(await grain.GetReminderVersionAsync());
        Assert.Equal(0, fixture.Reminders.FailedWrites(grain.GetGrainId()));
        Assert.DoesNotContain(fixture.Logs.Warnings, entry =>
            Equals(entry.Properties.GetValueOrDefault("GrainId"), grain.GetGrainId()));
    }

    [Fact]
    public async Task Deactivation_reuses_an_inherited_reminder_without_a_write()
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        await grain.PostAsync(inBackground: false);
        var version = await grain.GetReminderVersionAsync();
        await grain.PersistAsync("pending");
        await grain.DeactivateAsync();
        var activation = (await grain.ObserveAsync()).Activation;
        fixture.Reminders.RejectWrites(grain.GetGrainId());

        await grain.DeactivateAsync();

        Assert.NotEqual(activation, (await grain.ObserveAsync()).Activation);
        Assert.Equal(version, await grain.GetReminderVersionAsync());
        Assert.Equal(0, fixture.Reminders.FailedWrites(grain.GetGrainId()));
        Assert.DoesNotContain(fixture.Logs.Warnings, entry =>
            Equals(entry.Properties.GetValueOrDefault("GrainId"), grain.GetGrainId()));
    }

    [Fact]
    public async Task Constructor_registration_protects_pending_work_without_silo_configuration()
    {
        await using var unconfigured = new OutboxReminderFixture { ConfigureLifecycle = false };
        await unconfigured.InitializeAsync();
        var grain = unconfigured.GetUniqueGrain<IConstructorOutboxReminderGrain>();

        var activation = await grain.PersistAndDeactivateAsync();

        Assert.NotEqual(activation, await grain.GetActivationAsync());
        var reminders = await unconfigured.Reminders.ReadRows(grain.GetGrainId());
        Assert.Single(reminders.Reminders);
    }

    [Fact]
    public async Task Late_registration_without_silo_configuration_explains_the_required_setup()
    {
        await using var unconfigured = new OutboxReminderFixture { ConfigureLifecycle = false };
        await unconfigured.InitializeAsync();
        var grain = unconfigured.GetUniqueGrain<IOutboxReminderPolicyGrain>();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => grain.ObserveAsync());

        Assert.Contains("ConfigureOutboxProcessor()", error.Message, StringComparison.Ordinal);
        Assert.Contains("constructor", error.Message, StringComparison.Ordinal);
    }
}

public interface IConstructorOutboxReminderGrain : IGrainWithGuidKey
{
    Task<Guid> PersistAndDeactivateAsync();
    Task<Guid> GetActivationAsync();
}

public sealed class ConstructorOutboxReminderGrain : Grain, IConstructorOutboxReminderGrain, IOutboxGrain
{
    private readonly IPersistentState<OutboxReminderState> state;
    private readonly Guid activation = Guid.NewGuid();

    public ConstructorOutboxReminderGrain([PersistentState("state", "Payload")] IPersistentState<OutboxReminderState> state)
    {
        this.state = state;
        this.RegisterOutboxProcessor(() => state.State.Outbox,
            options => options.AcknowledgePosted = items => state.State.Outbox = state.State.Outbox.RemoveRange(items));
    }

    public async Task<Guid> PersistAndDeactivateAsync()
    {
        state.State.Outbox = state.State.Outbox.Add("pending");
        await state.WriteStateAsync();
        DeactivateOnIdle();
        return activation;
    }

    public Task<Guid> GetActivationAsync() => Task.FromResult(activation);
}

public interface IOutboxReminderPolicyGrain : IGrainWithGuidKey
{
    Task PublishAsync(string value, bool inBackground);
    Task PersistAsync(string value);
    Task PostAsync(bool inBackground);
    Task DeliverReminderAsync();
    Task UseDefaultPolicyAsync();
    Task<string?> GetReminderVersionAsync();
    Task<OutboxReminderObservation> ObserveAsync();
    Task DeactivateAsync();
    Task RejectDeliveryAsync();
    [AlwaysInterleave] Task WaitForDeliveryAsync();
}

[GenerateSerializer]
public sealed record OutboxReminderObservation(
    [property: Id(0)] int Delivered,
    [property: Id(1)] int Pending,
    [property: Id(2)] bool ReminderSeenByPostman,
    [property: Id(3)] Guid Activation);

[GenerateSerializer]
public sealed class OutboxReminderState
{
    [Id(0)] public Outbox<string> Outbox { get; set; } = [];
    [Id(1)] public int Delivered { get; set; }
    [Id(2)] public bool ReminderSeenByPostman { get; set; }
    [Id(3)] public OutboxReminderPolicy Policy { get; set; } = OutboxReminderPolicy.KeepRegistered;
}

public sealed class OutboxReminderPolicyGrain(
    [PersistentState("state", "Payload")] IPersistentState<OutboxReminderState> state,
    IReminderTable reminders) : Grain, IOutboxReminderPolicyGrain, IOutboxGrain
{
    private readonly Guid activation = Guid.NewGuid();
    private readonly TaskCompletionSource delivery = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private OutboxProcessor<string>? processor;
    private bool rejectDelivery;

    private OutboxProcessor<string> Processor => processor!;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        processor = this.RegisterOutboxProcessor(() => state.State.Outbox, options =>
        {
            options.ReminderPolicy = state.State.Policy;
            options.AcknowledgePostedAsync = AcknowledgeAsync;
            options.RetryDelay = TimeSpan.FromHours(1);
        }).AddPostman<string>(PostItemAsync);
        return base.OnActivateAsync(cancellationToken);
    }

    public async Task PublishAsync(string value, bool inBackground)
    {
        await PersistAsync(value);
        await PostAsync(inBackground);
    }

    public async Task PersistAsync(string value)
    {
        state.State.Outbox = state.State.Outbox.Add(value);
        await state.WriteStateAsync();
    }

    public Task PostAsync(bool inBackground) => inBackground
        ? Processor.PostInBackgroundAsync().AsTask()
        : Processor.PostAsync().AsTask();

    public Task DeliverReminderAsync()
    {
        // Deliver through the grain's public reminder entry point without waiting
        // for Orleans' minimum reminder period. Scheduling remains real Orleans work.
        return ((IRemindable)this).ReceiveReminder(Processor.ReminderName, default);
    }

    public async Task UseDefaultPolicyAsync()
    {
        state.State.Policy = OutboxReminderPolicy.OnRetry;
        await state.WriteStateAsync();
    }

    public async Task<string?> GetReminderVersionAsync() =>
        (await reminders.ReadRow(this.GetGrainId(), Processor.ReminderName))?.ETag;

    public Task<OutboxReminderObservation> ObserveAsync() => Task.FromResult(new OutboxReminderObservation(
        state.State.Delivered, state.State.Outbox.Count, state.State.ReminderSeenByPostman, activation));

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public Task RejectDeliveryAsync()
    {
        rejectDelivery = true;
        return Task.CompletedTask;
    }

    // The fixture's STJ storage provider is not activity-decorated. Signal after
    // persistence so recovery assertions wait for the real acknowledgement turn.
    public Task WaitForDeliveryAsync() => delivery.Task;

    private async ValueTask PostItemAsync(string value)
    {
        if (rejectDelivery)
        {
            throw new InvalidOperationException("Delivery is unavailable.");
        }

        state.State.ReminderSeenByPostman = await this.GetReminder(Processor.ReminderName) is not null;
    }

    private async ValueTask AcknowledgeAsync(
        ImmutableArray<OutboxMessageEnvelope<string>> items,
        CancellationToken cancellationToken)
    {
        state.State.Outbox = state.State.Outbox.RemoveRange(items);
        state.State.Delivered += items.Length;
        await state.WriteStateAsync(cancellationToken);
        delivery.TrySetResult();
    }
}
