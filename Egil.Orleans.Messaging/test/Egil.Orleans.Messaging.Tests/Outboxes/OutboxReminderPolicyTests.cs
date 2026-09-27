using System.Collections.Immutable;
using Orleans.Concurrency;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed partial class OutboxReminderPolicyTests(OutboxReminderFixture fixture)
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
    public async Task Returning_to_default_policy_leaves_an_inherited_reminder_without_a_local_handle()
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        await grain.PostAsync(inBackground: false);
        Assert.NotNull(await grain.GetReminderVersionAsync());

        await grain.UseDefaultPolicyAsync();
        await grain.DeactivateAsync();
        await grain.DeliverReminderAsync();
        await grain.DeactivateAsync();

        Assert.NotNull(await grain.GetReminderVersionAsync());
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
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
        Assert.Equal(new ReminderApiCounts(0, 2, 0), fixture.ReminderApi.For(grain.GetGrainId()));
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

    [Theory]
    [InlineData(OutboxReminderPolicy.OnRetry)]
    [InlineData(OutboxReminderPolicy.KeepRegistered)]
    public async Task Empty_deactivation_does_not_create_a_reminder_or_warn(OutboxReminderPolicy policy)
    {
        var grain = await CreateGrainAsync(policy);
        var activation = (await grain.ObserveAsync()).Activation;
        fixture.Reminders.RejectWrites(grain.GetGrainId());

        await grain.DeactivateAsync();

        Assert.NotEqual(activation, (await grain.ObserveAsync()).Activation);
        Assert.Null(await grain.GetReminderVersionAsync());
        Assert.Equal(0, fixture.Reminders.FailedWrites(grain.GetGrainId()));
        Assert.Equal(new ReminderApiCounts(0, 0, 0), fixture.ReminderApi.For(grain.GetGrainId()));
        Assert.DoesNotContain(fixture.Logs.Warnings, entry =>
            Equals(entry.Properties.GetValueOrDefault("GrainId"), grain.GetGrainId()));
    }

    [Fact]
    public async Task Deactivation_upserts_an_inherited_reminder_when_no_tick_has_established_its_existence()
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        await grain.PostAsync(inBackground: false);
        var version = await grain.GetReminderVersionAsync();
        await grain.PersistAsync("pending");
        await grain.DeactivateAsync();
        var activation = (await grain.ObserveAsync()).Activation;

        await grain.DeactivateAsync();

        Assert.NotEqual(activation, (await grain.ObserveAsync()).Activation);
        Assert.NotEqual(version, await grain.GetReminderVersionAsync());
        Assert.Equal(new ReminderApiCounts(0, 2, 0), fixture.ReminderApi.For(grain.GetGrainId()));
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

    [Fact]
    public async Task Cancellation_during_reminder_registration_cancels_the_background_post()
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        await grain.PersistAsync("pending");
        using var writes = fixture.Reminders.HoldWrites(grain.GetGrainId());
        var post = grain.PostWithCancellationAsync();
        await writes.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await grain.CancelPostAsync();
        writes.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => post);
        Assert.NotNull(await grain.GetReminderVersionAsync());
        Assert.Equal(1, (await grain.ObserveAsync()).Pending);
        await grain.PostAsync(inBackground: false);
        Assert.Equal(0, (await grain.ObserveAsync()).Pending);
        Assert.Equal(1, writes.Attempts);
    }

    [Theory]
    [InlineData(OutboxReminderPolicy.OnRetry, false)]
    [InlineData(OutboxReminderPolicy.OnRetry, true)]
    [InlineData(OutboxReminderPolicy.KeepRegistered, false)]
    [InlineData(OutboxReminderPolicy.KeepRegistered, true)]
    public async Task Overlapping_posts_share_the_initial_reminder_registration(
        OutboxReminderPolicy policy, bool firstInBackground)
    {
        var grain = await CreateGrainAsync(policy);
        await grain.RejectDeliveryAsync();
        await grain.PersistAsync("pending");
        using var writes = fixture.Reminders.HoldWrites(grain.GetGrainId());

        var posts = grain.PostConcurrentlyAsync(firstInBackground);
        await writes.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        writes.Release();
        await posts.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(1, writes.Attempts);
        Assert.NotNull(await grain.GetReminderVersionAsync());
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
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
    Task ConfigureReminderAsync(OutboxReminderPolicy policy, TimeSpan retryDelay);
    Task AllowDeliveryAsync();
    [AlwaysInterleave] Task DeliverReminderWhilePostingAsync();
    Task DeliverUnrelatedReminderAsync();
    Task PostAndDeactivateAsync();
    Task StartPostAsync();
    Task DropPendingAndDeactivateAsync();
    Task ObserveDeactivationAsync();
    Task<string?> GetReminderVersionAsync();
    Task<OutboxReminderObservation> ObserveAsync();
    Task DeactivateAsync();
    Task RejectDeliveryAsync();
    Task PostWithCancellationAsync();
    [AlwaysInterleave] Task CancelPostAsync();
    Task PostConcurrentlyAsync(bool firstInBackground);
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
    [Id(4)] public TimeSpan RetryDelay { get; set; } = TimeSpan.FromHours(1);
}

public sealed class OutboxReminderPolicyGrain(
    [PersistentState("state", "Payload")] IPersistentState<OutboxReminderState> state,
    IReminderTable reminders,
    OutboxDeactivationProbe deactivations) : Grain, IOutboxReminderPolicyGrain, IOutboxGrain
{
    private readonly Guid activation = Guid.NewGuid();
    private readonly TaskCompletionSource delivery = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private OutboxProcessor<string>? processor;
    private bool rejectDelivery;
    private CancellationTokenSource? postCancellation;

    private OutboxProcessor<string> Processor => processor!;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        processor = this.RegisterOutboxProcessor(() => state.State.Outbox, options =>
        {
            options.ReminderPolicy = state.State.Policy;
            options.AcknowledgePostedAsync = AcknowledgeAsync;
            options.RetryDelay = state.State.RetryDelay;
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

    public async Task PostWithCancellationAsync()
    {
        using var cancellation = new CancellationTokenSource();
        postCancellation = cancellation;
        try
        {
            await Processor.PostInBackgroundAsync(cancellation.Token);
        }
        finally
        {
            postCancellation = null;
        }
    }

    public Task CancelPostAsync() => postCancellation!.CancelAsync();

    public Task PostConcurrentlyAsync(bool firstInBackground)
    {
        // Both calls begin on the activation scheduler while the first reminder
        // write is held at the storage boundary, forcing their awaits to overlap.
        var first = PostAsync(firstInBackground);
        var second = PostAsync(inBackground: true);
        return Task.WhenAll(first, second);
    }

    public Task DeliverReminderAsync()
    {
        // Deliver through the grain's public reminder entry point without waiting
        // for Orleans' minimum reminder period. Scheduling remains real Orleans work.
        return ((IRemindable)this).ReceiveReminder(Processor.ReminderName, default);
    }

    public Task DeliverReminderWhilePostingAsync() => DeliverReminderAsync();

    public Task DeliverUnrelatedReminderAsync() => ((IRemindable)this).ReceiveReminder("unrelated", default);

    public async Task ConfigureReminderAsync(OutboxReminderPolicy policy, TimeSpan retryDelay)
    {
        state.State.Policy = policy;
        state.State.RetryDelay = retryDelay;
        await state.WriteStateAsync();
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

    public Task AllowDeliveryAsync()
    {
        rejectDelivery = false;
        return Task.CompletedTask;
    }

    public Task PostAndDeactivateAsync()
    {
        // Keep the registration alive beyond this turn so Orleans' real shutdown
        // hook must coordinate with it, rather than a test calling that hook itself.
        _ = Processor.PostInBackgroundAsync().AsTask();
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public Task StartPostAsync()
    {
        // Let another grain turn drop the pending work while the registration
        // response is held, so shutdown must await a handle it does not yet have.
        _ = Processor.PostAsync().AsTask();
        return Task.CompletedTask;
    }

    public async Task DropPendingAndDeactivateAsync()
    {
        state.State.Outbox = [];
        await state.WriteStateAsync();
        DeactivateOnIdle();
    }

    public Task ObserveDeactivationAsync()
    {
        deactivations.Observe(GrainContext);
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

        state.State.ReminderSeenByPostman = await reminders.ReadRow(this.GetGrainId(), Processor.ReminderName) is not null;
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
