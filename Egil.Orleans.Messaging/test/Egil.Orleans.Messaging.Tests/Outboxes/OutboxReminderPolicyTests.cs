using System.Collections.Immutable;
using Orleans.Concurrency;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed partial class OutboxReminderPolicyTests(OutboxReminderFixture fixture)
    : IClassFixture<OutboxReminderFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Successful_default_posts_make_no_reminder_calls(bool inBackground)
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnRetry);
        await grain.PublishAsync("first", inBackground);
        await grain.WaitForDeliveryAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await grain.PostAsync(inBackground);
        await grain.PublishAsync("second", false);
        Assert.Equal(2, (await grain.ObserveAsync()).Delivered);
        Assert.Equal(new ReminderApiCounts(0, 0, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Keep_registered_activation_establishes_a_slow_fallback_reused_by_successful_posts(bool inBackground)
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.KeepRegistered);
        await fixture.ReminderApi.WaitForRegistrationAsync(grain.GetGrainId())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var row = Assert.Single((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        Assert.Equal(TimeSpan.FromHours(2), row.Period);

        await grain.PublishAsync("first", inBackground);
        await grain.WaitForDeliveryAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await grain.PublishAsync("second", false);
        await grain.PostAsync(inBackground);
        Assert.Equal(2, (await grain.ObserveAsync()).Delivered);
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Theory]
    [InlineData(OutboxReminderPolicy.OnRetry, 1, 1)]
    [InlineData(OutboxReminderPolicy.KeepRegistered, 3, 0)]
    public async Task Retry_transitions_make_only_the_required_reminder_calls(
        OutboxReminderPolicy policy, int registrations, int removals)
    {
        var grain = await CreateGrainAsync(policy);
        await grain.RejectDeliveryAsync();
        await grain.PublishAsync("pending", false);
        var retry = Assert.Single((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        Assert.Equal(TimeSpan.FromHours(1), retry.Period);
        await grain.PostAsync(false);
        await grain.AllowDeliveryAsync();
        await grain.PostAsync(false);
        await grain.PublishAsync("later", false);
        Assert.Equal(new ReminderApiCounts(0, registrations, removals), fixture.ReminderApi.For(grain.GetGrainId()));
        Assert.Equal(2, (await grain.ObserveAsync()).Delivered);
    }

    [Fact]
    public async Task Keep_registered_returns_to_slow_fallback_after_retry_and_removes_it_on_empty_deactivation()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.KeepRegistered);
        await grain.RejectDeliveryAsync();
        await grain.PublishAsync("pending", false);
        await grain.AllowDeliveryAsync();
        await grain.PostAsync(false);
        var row = Assert.Single((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        Assert.Equal(TimeSpan.FromHours(2), row.Period);
        await grain.ObserveDeactivationAsync();
        await grain.DeactivateAsync();
        await fixture.Deactivations.WaitForCompletionAsync(grain.GetGrainId())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Empty((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        Assert.Equal(new ReminderApiCounts(0, 3, 1), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Fact]
    public async Task Inherited_reminder_is_looked_up_only_when_recovery_finishes()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnRetry);
        await grain.PersistAsync("pending");
        await grain.DeactivateAsync();
        await grain.ObserveAsync();
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
        await grain.DeliverReminderAsync();
        await grain.PostAsync(false);
        Assert.Equal(0, (await grain.ObserveAsync()).Pending);
        Assert.Null(await grain.GetReminderVersionAsync());
        Assert.Equal(new ReminderApiCounts(1, 1, 1), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Fact]
    public async Task Stale_empty_tick_does_not_suppress_a_later_required_registration()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnRetry);
        await grain.DeliverReminderAsync();
        await grain.RejectDeliveryAsync();
        await grain.PublishAsync("pending", false);
        Assert.NotNull(await grain.GetReminderVersionAsync());
        Assert.Equal(new ReminderApiCounts(1, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Fact]
    public async Task Stale_tick_with_pending_work_does_not_replace_a_durable_retry_registration()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnRetry);
        await grain.RejectDeliveryAsync();
        await grain.PersistAsync("pending");
        await grain.DeliverReminderAsync();
        await grain.PostAsync(false);
        Assert.NotNull(await grain.GetReminderVersionAsync());
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Fact]
    public async Task Unseen_inherited_reminder_is_upserted_without_a_lookup_when_retry_is_needed()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnRetry);
        await grain.PersistAsync("pending");
        await grain.DeactivateAsync();
        await grain.RejectDeliveryAsync();
        await grain.PostAsync(false);
        Assert.Equal(new ReminderApiCounts(0, 2, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Fact]
    public async Task A_lost_registration_response_is_retried_without_a_lookup()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnRetry);
        await grain.RejectDeliveryAsync();
        fixture.ReminderApi.LoseNextRegistrationResponse(grain.GetGrainId());
        await Assert.ThrowsAsync<TimeoutException>(() => grain.PublishAsync("pending", false));
        await grain.AllowDeliveryAsync();
        await grain.DeliverReminderAsync();
        await grain.PostAsync(false);
        Assert.Null(await grain.GetReminderVersionAsync());
        Assert.Equal(0, fixture.ReminderApi.For(grain.GetGrainId()).Lookups);
    }

    [Fact]
    public async Task Failed_registration_is_retryable_without_a_lookup()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnRetry);
        await grain.RejectDeliveryAsync();
        fixture.Reminders.RejectWrites(grain.GetGrainId());
        await Assert.ThrowsAnyAsync<Exception>(() => grain.PublishAsync("pending", false));
        Assert.Null(await grain.GetReminderVersionAsync());
        fixture.Reminders.AllowWrites(grain.GetGrainId());
        await grain.PostAsync(false);
        Assert.NotNull(await grain.GetReminderVersionAsync());
        Assert.Equal(0, fixture.ReminderApi.For(grain.GetGrainId()).Lookups);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Overlapping_posts_share_the_initial_retry_registration(bool firstInBackground)
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnRetry);
        await grain.RejectDeliveryAsync();
        await grain.PersistAsync("pending");
        using var writes = fixture.Reminders.HoldWrites(grain.GetGrainId());
        var posts = grain.PostConcurrentlyAsync(firstInBackground);
        await writes.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        writes.Release();
        await posts.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await grain.PostAsync(false);
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Fact]
    public async Task Constructor_registration_protects_pending_work_without_silo_configuration()
    {
        await using var unconfigured = new OutboxReminderFixture { ConfigureLifecycle = false };
        await unconfigured.InitializeAsync();
        var grain = unconfigured.GetUniqueGrain<IConstructorOutboxReminderGrain>();
        var activation = await grain.PersistAndDeactivateAsync();
        Assert.NotEqual(activation, await grain.GetActivationAsync());
        Assert.Single((await unconfigured.Reminders.ReadRows(grain.GetGrainId())).Reminders);
    }

    [Fact]
    public async Task Late_registration_without_silo_configuration_explains_the_required_setup()
    {
        await using var unconfigured = new OutboxReminderFixture { ConfigureLifecycle = false };
        await unconfigured.InitializeAsync();
        var grain = unconfigured.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => grain.ObserveAsync());
        Assert.Contains("ConfigureOutboxProcessor()", error.Message, StringComparison.Ordinal);
    }
}

public interface IConstructorFallbackReminderGrain : IGrainWithGuidKey
{
    Task PingAsync();
}

public sealed class ConstructorFallbackReminderGrain : Grain, IConstructorFallbackReminderGrain, IOutboxGrain
{
    public ConstructorFallbackReminderGrain()
    {
        this.RegisterOutboxProcessor(static Outbox<string> () => [], options =>
        {
            options.ReminderPolicy = OutboxReminderPolicy.KeepRegistered;
            options.AcknowledgePosted = static _ => { };
        });
    }

    public Task PingAsync() => Task.CompletedTask;
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
    [Id(3)] public OutboxReminderPolicy Policy { get; set; } = OutboxReminderPolicy.OnRetry;
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
            options.IdleReminderPeriod = TimeSpan.FromHours(2);
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
