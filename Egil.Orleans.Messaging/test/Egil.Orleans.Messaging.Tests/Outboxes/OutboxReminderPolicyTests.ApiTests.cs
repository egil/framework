namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed partial class OutboxReminderPolicyTests
{
    [Fact]
    public async Task Empty_deactivation_waits_for_the_registration_handle_before_removing_the_reminder()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnRetry);
        await grain.RejectDeliveryAsync();
        await grain.PersistAsync("pending");
        var activation = (await grain.ObserveAsync()).Activation;
        using var response = fixture.ReminderApi.HoldRegistrationResponse(grain.GetGrainId());
        await grain.ObserveDeactivationAsync();
        await grain.StartPostAsync();
        await response.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await grain.DropPendingAndDeactivateAsync();
        await fixture.Deactivations.WaitForEntryAsync(grain.GetGrainId())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var reactivation = grain.ObserveAsync();
        response.Release();

        var state = await reactivation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.NotEqual(activation, state.Activation);
        Assert.Equal(0, state.Pending);
        Assert.Equal(new ReminderApiCounts(0, 1, 1), fixture.ReminderApi.For(grain.GetGrainId()));
        Assert.Null(await grain.GetReminderVersionAsync());
    }

    [Fact]
    public async Task Failed_removal_warns_without_blocking_deactivation_or_claiming_pending_work()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnRetry);
        await grain.RejectDeliveryAsync();
        await grain.PublishAsync("first", inBackground: false);
        var activation = (await grain.ObserveAsync()).Activation;
        fixture.ReminderApi.RejectRemoval(grain.GetGrainId());

        await grain.DropPendingAndDeactivateAsync();

        Assert.NotEqual(activation, (await grain.ObserveAsync()).Activation);
        Assert.Equal(new ReminderApiCounts(0, 1, 1), fixture.ReminderApi.For(grain.GetGrainId()));
        var warning = Assert.Single(fixture.Logs.Warnings, entry =>
            Equals(entry.Properties.GetValueOrDefault("GrainId"), grain.GetGrainId()));
        Assert.Equal("OutboxReminderRemovalFailed", warning.EventId.Name);
        Assert.DoesNotContain("manual reactivation", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await grain.GetReminderVersionAsync());
    }

    [Fact]
    public async Task Unused_fallback_is_removed_on_deactivation_without_firing_or_lookup()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.KeepRegistered);
        await fixture.ReminderApi.WaitForRegistrationAsync(grain.GetGrainId())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await grain.ObserveDeactivationAsync();
        await grain.DeactivateAsync();
        await fixture.Deactivations.WaitForCompletionAsync(grain.GetGrainId())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Empty((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        Assert.Equal(new ReminderApiCounts(0, 1, 1), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Fact]
    public async Task Pending_deactivation_uses_the_configured_retry_period_without_lookup()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.KeepRegistered);
        await grain.PersistAsync("pending");
        await grain.ObserveDeactivationAsync();
        await grain.DeactivateAsync();
        await fixture.Deactivations.WaitForCompletionAsync(grain.GetGrainId())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var row = Assert.Single((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        Assert.Equal(TimeSpan.FromHours(1), row.Period);
        Assert.Equal(new ReminderApiCounts(0, 2, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Fact]
    public async Task Failed_deactivation_registration_warns_and_leaves_pending_work_recoverable()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnRetry);
        await grain.PersistAsync("pending");
        await grain.ObserveDeactivationAsync();
        fixture.Reminders.RejectWrites(grain.GetGrainId());
        await grain.DeactivateAsync();
        await fixture.Deactivations.WaitForCompletionAsync(grain.GetGrainId())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var warning = Assert.Single(fixture.Logs.Warnings, entry =>
            Equals(entry.Properties.GetValueOrDefault("GrainId"), grain.GetGrainId()));
        Assert.Equal("OutboxDeactivationReminderFailed", warning.EventId.Name);
        Assert.Contains("manual reactivation", warning.Message, StringComparison.OrdinalIgnoreCase);
        fixture.Reminders.AllowWrites(grain.GetGrainId());
        await grain.PostAsync(false);
        Assert.Equal(0, (await grain.ObserveAsync()).Pending);
    }

    [Fact]
    public async Task Failed_drain_cleanup_is_retried_without_another_registration_or_lookup()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnRetry);
        await grain.RejectDeliveryAsync();
        await grain.PublishAsync("pending", false);
        fixture.ReminderApi.RejectRemoval(grain.GetGrainId());
        await grain.AllowDeliveryAsync();
        await grain.PostAsync(false);
        Assert.Equal(0, (await grain.ObserveAsync()).Pending);
        fixture.ReminderApi.AllowRemoval(grain.GetGrainId());
        await grain.PostAsync(false);
        Assert.Null(await grain.GetReminderVersionAsync());
        Assert.Equal(new ReminderApiCounts(0, 1, 2), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Fact]
    public async Task Activation_registration_and_an_immediate_post_share_the_same_write()
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        await grain.ConfigureReminderAsync(OutboxReminderPolicy.KeepRegistered, TimeSpan.FromHours(1));
        await grain.DeactivateAsync();
        using var response = fixture.ReminderApi.HoldRegistrationResponse(grain.GetGrainId());
        var post = grain.PublishAsync("pending", false);
        await response.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        response.Release();
        await post;
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Fact]
    public async Task Constructor_attached_fallback_is_registered_before_the_first_grain_call()
    {
        var grain = fixture.GetUniqueGrain<IConstructorFallbackReminderGrain>();
        await grain.PingAsync();
        var row = Assert.Single((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        Assert.Equal(TimeSpan.FromHours(1), row.Period);
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    private async Task<IOutboxReminderPolicyGrain> CreateGrainAsync(OutboxReminderPolicy policy)
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        await grain.ConfigureReminderAsync(policy, TimeSpan.FromHours(1));
        await grain.DeactivateAsync();
        await grain.ObserveAsync();
        return grain;
    }
}
