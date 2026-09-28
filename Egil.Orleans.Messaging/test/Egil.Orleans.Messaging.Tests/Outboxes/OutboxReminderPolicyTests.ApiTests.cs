namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed partial class OutboxReminderPolicyTests
{
    [Fact]
    public async Task Empty_deactivation_waits_for_the_registration_handle_before_removing_the_reminder()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.KeepRegistered);
        await grain.PostAsync(false);
        await grain.RejectDeliveryAsync();
        await grain.PersistAsync("pending");
        using var response = fixture.ReminderApi.HoldRegistrationResponse(grain.GetGrainId());
        await grain.ObserveDeactivationAsync();
        await grain.StartPostAsync();
        await response.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await grain.DropPendingAndDeactivateAsync();
        await fixture.Deactivations.WaitForEntryAsync(grain.GetGrainId())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        response.Release();

        await fixture.Deactivations.WaitForCompletionAsync(grain.GetGrainId())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(new ReminderApiCounts(0, 2, 1), fixture.ReminderApi.For(grain.GetGrainId()));
        Assert.Empty((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
    }

    [Fact]
    public async Task Failed_removal_warns_without_blocking_deactivation_or_claiming_pending_work()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.KeepRegistered);
        await grain.PostAsync(false);
        await grain.ObserveDeactivationAsync();
        fixture.ReminderApi.RejectRemoval(grain.GetGrainId());
        await grain.DeactivateAsync();
        await fixture.Deactivations.WaitForCompletionAsync(grain.GetGrainId())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(new ReminderApiCounts(0, 1, 1), fixture.ReminderApi.For(grain.GetGrainId()));
        var warning = Assert.Single(fixture.Logs.Warnings, entry =>
            Equals(entry.Properties.GetValueOrDefault("GrainId"), grain.GetGrainId()));
        Assert.Equal("OutboxReminderRemovalFailed", warning.EventId.Name);
        Assert.DoesNotContain("manual reactivation", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
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

    [Theory]
    [InlineData(OutboxReminderPolicy.OnDeactivation, 1)]
    [InlineData(OutboxReminderPolicy.KeepRegistered, 2)]
    public async Task Pending_deactivation_uses_the_active_recovery_period_instead_of_the_timer_delay(
        OutboxReminderPolicy policy, int registrations)
    {
        var grain = await CreateGrainAsync(policy);
        await grain.PersistAsync("pending");
        await grain.ObserveDeactivationAsync();
        await grain.DeactivateAsync();
        await fixture.Deactivations.WaitForCompletionAsync(grain.GetGrainId())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var row = Assert.Single((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        Assert.Equal(TimeSpan.FromMinutes(10), row.Period);
        Assert.Equal(new ReminderApiCounts(0, registrations, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Fact]
    public async Task Failed_deactivation_registration_warns_and_leaves_pending_work_recoverable()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnDeactivation);
        await grain.PersistAsync("pending");
        await grain.ObserveDeactivationAsync();
        fixture.Reminders.RejectWrites(grain.GetGrainId());
        await grain.DeactivateAsync();
        await fixture.Deactivations.WaitForCompletionAsync(grain.GetGrainId())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var warning = Assert.Single(fixture.Logs.Warnings, entry =>
            Equals(entry.Properties.GetValueOrDefault("GrainId"), grain.GetGrainId()));
        Assert.Equal("OutboxDeactivationReminderFailed", warning.EventId.Name);
        Assert.Equal(1, warning.Properties["OutboxItemCount"]);
        Assert.Contains("manual reactivation", warning.Message, StringComparison.OrdinalIgnoreCase);
        fixture.Reminders.AllowWrites(grain.GetGrainId());
        await grain.PostAsync(false);
        Assert.Equal(0, (await grain.ObserveAsync()).Pending);
    }

    [Fact]
    public async Task Failed_inherited_cleanup_is_retried_without_another_registration()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnDeactivation);
        await grain.PersistAsync("pending");
        await grain.DeactivateAsync();
        await grain.ClearPendingAsync();
        fixture.ReminderApi.RejectRemoval(grain.GetGrainId());
        await grain.DeliverReminderAsync();
        fixture.ReminderApi.AllowRemoval(grain.GetGrainId());
        await grain.PostAsync(false);
        Assert.Null(await grain.GetReminderVersionAsync());
        Assert.Equal(new ReminderApiCounts(2, 1, 2), fixture.ReminderApi.For(grain.GetGrainId()));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_idle_adjustment_preserves_successful_delivery_and_can_be_retried(bool inBackground)
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.KeepRegistered);
        await grain.RejectDeliveryAsync();
        await grain.PublishAsync("pending", false);
        fixture.Reminders.RejectWrites(grain.GetGrainId());
        await grain.AllowDeliveryAsync();

        await grain.PostAsync(inBackground);
        await grain.PostAsync(false);

        var observation = await grain.ObserveAsync();
        Assert.Equal(1, observation.Delivered);
        Assert.Equal(0, observation.Pending);
        var active = Assert.Single((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        Assert.Equal(TimeSpan.FromMinutes(10), active.Period);
        Assert.Contains(fixture.Logs.Warnings, entry =>
            entry.EventId.Name == "OutboxReminderAdjustmentFailed"
            && Equals(entry.Properties.GetValueOrDefault("GrainId"), grain.GetGrainId()));

        fixture.Reminders.AllowWrites(grain.GetGrainId());
        await grain.PostAsync(false);
        var idle = Assert.Single((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        Assert.Equal(TimeSpan.FromHours(2), idle.Period);
        Assert.Equal(0, fixture.ReminderApi.For(grain.GetGrainId()).Lookups);
    }

    [Fact]
    public async Task Activation_with_pending_work_preserves_the_active_recovery_period()
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        await grain.ConfigureReminderAsync(OutboxReminderPolicy.KeepRegistered, TimeSpan.FromHours(1));
        await grain.PersistAsync("pending");
        await grain.ObserveDeactivationAsync();
        await grain.DeactivateAsync();
        await fixture.Deactivations.WaitForCompletionAsync(grain.GetGrainId())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        using var response = fixture.ReminderApi.HoldRegistrationResponse(grain.GetGrainId());
        await grain.ObserveAsync();
        await response.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        response.Release();
        var row = Assert.Single((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        Assert.Equal(TimeSpan.FromMinutes(10), row.Period);
        Assert.Equal(1, (await grain.ObserveAsync()).Pending);
    }

    [Fact]
    public async Task Constructor_attached_fallback_failure_prevents_activation()
    {
        var grain = fixture.GetUniqueGrain<IConstructorFallbackReminderGrain>();
        fixture.Reminders.RejectWrites(grain.GetGrainId());

        await Assert.ThrowsAnyAsync<Exception>(() => grain.PingAsync());

        Assert.True(fixture.Reminders.FailedWrites(grain.GetGrainId()) > 0);
        Assert.Empty((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        fixture.Reminders.AllowWrites(grain.GetGrainId());
        await grain.PingAsync();
        Assert.Single((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
    }
    private async Task<IOutboxReminderPolicyGrain> CreateGrainAsync(OutboxReminderPolicy policy, TimeSpan? retryDelay = null)
    {
        var grain = fixture.GetUniqueGrain<IOutboxReminderPolicyGrain>();
        await grain.ConfigureReminderAsync(policy, retryDelay ?? TimeSpan.FromHours(1));
        await grain.DeactivateAsync();
        await grain.ObserveAsync();
        return grain;
    }
}
