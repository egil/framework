namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed partial class OutboxReminderPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Successful_default_posts_and_empty_ticks_make_no_reminder_calls(bool inBackground)
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnRetry);

        await grain.PublishAsync("first", inBackground);
        await grain.WaitForDeliveryAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await grain.PostAsync(inBackground);
        await grain.DeliverReminderAsync();
        await grain.PublishAsync("second", inBackground: false);

        Assert.Equal(2, (await grain.ObserveAsync()).Delivered);
        Assert.Equal(new ReminderApiCounts(0, 0, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Theory]
    [InlineData(OutboxReminderPolicy.OnRetry)]
    [InlineData(OutboxReminderPolicy.KeepRegistered)]
    public async Task Failed_deliveries_and_later_batches_reuse_one_registration(OutboxReminderPolicy policy)
    {
        var grain = await CreateGrainAsync(policy);
        await grain.RejectDeliveryAsync();

        await grain.PublishAsync("first", inBackground: false);
        await grain.PostAsync(inBackground: false);
        await grain.AllowDeliveryAsync();
        await grain.PostAsync(inBackground: false);
        await grain.DeliverReminderAsync();
        await grain.PublishAsync("second", inBackground: false);

        Assert.Equal(2, (await grain.ObserveAsync()).Delivered);
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Theory]
    [InlineData(OutboxReminderPolicy.OnRetry)]
    [InlineData(OutboxReminderPolicy.KeepRegistered)]
    public async Task An_inherited_tick_avoids_registration_for_pending_work_and_deactivation(OutboxReminderPolicy policy)
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.KeepRegistered);
        await grain.PostAsync(inBackground: false);
        await grain.ConfigureReminderAsync(policy, TimeSpan.FromHours(1));
        await grain.DeactivateAsync();
        await grain.DeliverReminderAsync();
        await grain.RejectDeliveryAsync();
        await grain.PublishAsync("pending", inBackground: false);
        var activation = (await grain.ObserveAsync()).Activation;

        await grain.DeactivateAsync();

        var state = await grain.ObserveAsync();
        Assert.NotEqual(activation, state.Activation);
        Assert.Equal(1, state.Pending);
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Theory]
    [InlineData(OutboxReminderPolicy.OnRetry)]
    [InlineData(OutboxReminderPolicy.KeepRegistered)]
    public async Task Unrelated_ticks_do_not_suppress_required_registration(OutboxReminderPolicy policy)
    {
        var grain = await CreateGrainAsync(policy);
        await grain.RejectDeliveryAsync();

        await grain.DeliverUnrelatedReminderAsync();
        await grain.PublishAsync("pending", inBackground: false);

        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
        Assert.NotNull(await grain.GetReminderVersionAsync());
    }

    [Theory]
    [InlineData(OutboxReminderPolicy.OnRetry, 1)]
    [InlineData(OutboxReminderPolicy.KeepRegistered, 0)]
    public async Task Empty_deactivation_removes_only_a_locally_owned_default_reminder(
        OutboxReminderPolicy policy, int removals)
    {
        var grain = await CreateGrainAsync(policy);
        await grain.RejectDeliveryAsync();
        await grain.PublishAsync("first", inBackground: false);
        await grain.AllowDeliveryAsync();
        await grain.PostAsync(inBackground: false);
        var activation = (await grain.ObserveAsync()).Activation;
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));

        await grain.DeactivateAsync();

        Assert.NotEqual(activation, (await grain.ObserveAsync()).Activation);
        Assert.Equal(new ReminderApiCounts(0, 1, removals), fixture.ReminderApi.For(grain.GetGrainId()));
        Assert.Equal(removals == 0, await grain.GetReminderVersionAsync() is not null);
    }

    [Theory]
    [InlineData(OutboxReminderPolicy.OnRetry)]
    [InlineData(OutboxReminderPolicy.KeepRegistered)]
    public async Task Pending_deactivation_registers_once_and_retains_the_known_reminder(OutboxReminderPolicy policy)
    {
        var grain = await CreateGrainAsync(policy);
        await grain.PersistAsync("pending");
        var activation = (await grain.ObserveAsync()).Activation;

        await grain.DeactivateAsync();

        Assert.NotEqual(activation, (await grain.ObserveAsync()).Activation);
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
        await grain.DeliverReminderAsync();
        await grain.WaitForDeliveryAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(0, (await grain.ObserveAsync()).Pending);
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Fact]
    public async Task A_post_before_an_inherited_tick_upserts_with_the_current_period()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.KeepRegistered);
        await grain.PostAsync(inBackground: false);
        var version = await grain.GetReminderVersionAsync();
        await grain.ConfigureReminderAsync(OutboxReminderPolicy.KeepRegistered, TimeSpan.FromHours(2));
        await grain.DeactivateAsync();

        await grain.PostAsync(inBackground: false);

        Assert.NotEqual(version, await grain.GetReminderVersionAsync());
        var row = Assert.Single((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        Assert.Equal(TimeSpan.FromHours(2), row.Period);
        Assert.Equal(new ReminderApiCounts(0, 2, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Fact]
    public async Task A_tick_recovers_a_lost_registration_response_without_another_call()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.KeepRegistered);
        fixture.ReminderApi.LoseNextRegistrationResponse(grain.GetGrainId());
        await Assert.ThrowsAsync<TimeoutException>(() => grain.PostAsync(inBackground: false));
        Assert.NotNull(await grain.GetReminderVersionAsync());

        await grain.DeliverReminderAsync();
        await grain.PublishAsync("recovered", inBackground: false);

        Assert.Equal(1, (await grain.ObserveAsync()).Delivered);
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Fact]
    public async Task A_tick_during_a_failed_registration_remains_evidence_of_the_reminder()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.KeepRegistered);
        fixture.ReminderApi.LoseNextRegistrationResponse(grain.GetGrainId());
        using var response = fixture.ReminderApi.HoldRegistrationResponse(grain.GetGrainId());
        var post = grain.PostAsync(inBackground: false);
        await response.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await grain.DeliverReminderWhilePostingAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        response.Release();
        await Assert.ThrowsAsync<TimeoutException>(() => post);
        await grain.PostAsync(inBackground: false);

        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
    }

    [Fact]
    public async Task Deactivation_shares_an_in_progress_registration_for_pending_work()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.KeepRegistered);
        await grain.PersistAsync("pending");
        var activation = (await grain.ObserveAsync()).Activation;
        using var writes = fixture.Reminders.HoldWrites(grain.GetGrainId());
        await grain.ObserveDeactivationAsync();

        await grain.PostAndDeactivateAsync();
        await writes.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.Deactivations.WaitForEntryAsync(grain.GetGrainId())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        writes.Release();

        Assert.NotEqual(activation, (await grain.ObserveAsync()).Activation);
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
        Assert.NotNull(await grain.GetReminderVersionAsync());
    }

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
        await grain.AllowDeliveryAsync();
        await grain.PostAsync(inBackground: false);
        var activation = (await grain.ObserveAsync()).Activation;
        fixture.ReminderApi.RejectRemoval(grain.GetGrainId());

        await grain.DeactivateAsync();

        Assert.NotEqual(activation, (await grain.ObserveAsync()).Activation);
        Assert.Equal(new ReminderApiCounts(0, 1, 1), fixture.ReminderApi.For(grain.GetGrainId()));
        var warning = Assert.Single(fixture.Logs.Warnings, entry =>
            Equals(entry.Properties.GetValueOrDefault("GrainId"), grain.GetGrainId()));
        Assert.Equal("OutboxDeactivationReminderRemovalFailed", warning.EventId.Name);
        Assert.DoesNotContain("manual reactivation", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await grain.GetReminderVersionAsync());
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
