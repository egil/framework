namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class FencedOutboxDeactivationTests(OutboxReminderFixture fixture)
    : IClassFixture<OutboxReminderFixture>
{
    [Theory]
    [InlineData(OutboxReminderPolicy.OnDeactivation, StorageFailureKind.UnknownOutcome, true)]
    [InlineData(OutboxReminderPolicy.KeepRegistered, StorageFailureKind.UnknownOutcome, true)]
    [InlineData(OutboxReminderPolicy.OnDeactivation, StorageFailureKind.UnknownOutcome, false)]
    [InlineData(OutboxReminderPolicy.OnDeactivation, StorageFailureKind.DidNotPersist, false)]
    [InlineData(OutboxReminderPolicy.KeepRegistered, StorageFailureKind.DidNotPersist, false)]
    public async Task Uncertain_pending_work_gets_a_reminder_and_recovers_from_durable_state(
        OutboxReminderPolicy policy, StorageFailureKind kind, bool persisted)
    {
        var grain = await CreateGrainAsync(policy, kind);

        var activation = await grain.FailWriteAsync(kind, persisted);
        await WaitForDeactivationAsync(grain);

        var row = Assert.Single((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        Assert.Equal(TimeSpan.FromMinutes(10), row.Period);
        Assert.Equal(new ReminderApiCounts(0, policy == OutboxReminderPolicy.KeepRegistered ? 2 : 1, 0),
            fixture.ReminderApi.For(grain.GetGrainId()));
        AssertNoWarnings(grain);

        var recovered = await grain.RecoverAsync();
        Assert.NotEqual(activation, recovered.Activation);
        Assert.Equal(persisted ? 1 : 0, recovered.Delivered);
        Assert.Equal(0, recovered.Pending);
        if (policy == OutboxReminderPolicy.OnDeactivation)
            Assert.Empty((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
    }

    [Theory]
    [InlineData(OutboxReminderPolicy.OnDeactivation, false)]
    [InlineData(OutboxReminderPolicy.KeepRegistered, false)]
    [InlineData(OutboxReminderPolicy.OnDeactivation, true)]
    [InlineData(OutboxReminderPolicy.KeepRegistered, true)]
    public async Task Confirmed_conflict_does_not_register_or_remove_a_reminder(OutboxReminderPolicy policy, bool staged)
    {
        var grain = await CreateGrainAsync(policy, StorageFailureKind.Conflict);
        var before = (await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders.ToArray();
        var calls = fixture.ReminderApi.For(grain.GetGrainId());

        await grain.FailWriteAsync(StorageFailureKind.Conflict, false, staged);
        await WaitForDeactivationAsync(grain);

        Assert.Equal(calls, fixture.ReminderApi.For(grain.GetGrainId()));
        var after = (await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders;
        Assert.Equal(before.Select(row => (row.ETag, row.Period)), after.Select(row => (row.ETag, row.Period)));
        if (policy == OutboxReminderPolicy.KeepRegistered)
            Assert.Single(after);
        AssertNoWarnings(grain);
    }

    [Theory]
    [InlineData(StorageFailureKind.UnknownOutcome, false, false)]
    [InlineData(StorageFailureKind.DidNotPersist, false, false)]
    [InlineData(StorageFailureKind.UnknownOutcome, true, false)]
    [InlineData(StorageFailureKind.DidNotPersist, true, false)]
    [InlineData(StorageFailureKind.UnknownOutcome, false, true)]
    [InlineData(StorageFailureKind.DidNotPersist, false, true)]
    public async Task Failed_fallback_registration_warns_with_the_available_count(
        StorageFailureKind kind, bool staged, bool unreadable)
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnDeactivation, kind);
        fixture.Reminders.RejectWrites(grain.GetGrainId());

        await grain.FailWriteAsync(kind, false, staged, unreadable);
        await WaitForDeactivationAsync(grain);

        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
        Assert.Empty((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        var warning = Assert.Single(fixture.Logs.Warnings, entry =>
            Equals(entry.Properties.GetValueOrDefault("GrainId"), grain.GetGrainId()));
        Assert.Equal("OutboxDeactivationReminderFailed", warning.EventId.Name);
        Assert.Equal(unreadable ? null : (object)(staged ? 1 : 0), warning.Properties["OutboxItemCount"]);
        Assert.IsNotType<StateManagerFencedException>(warning.Exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_conflict_on_another_state_manager_cannot_hide_an_uncertain_outcome(bool conflictFirst)
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnDeactivation, StorageFailureKind.UnknownOutcome);

        await grain.FailTwoManagersAsync(conflictFirst);
        await WaitForDeactivationAsync(grain);

        Assert.Single((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
        AssertNoWarnings(grain);
    }

    [Theory]
    [InlineData(StorageFailureKind.UnknownOutcome)]
    [InlineData(StorageFailureKind.DidNotPersist)]
    public async Task An_unrelated_accessor_failure_cannot_suppress_recorded_fence_recovery(StorageFailureKind kind)
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnDeactivation, kind);

        await grain.FailWithBrokenAccessorAsync(kind);
        await WaitForDeactivationAsync(grain);

        Assert.Single((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
        AssertNoWarnings(grain);
        var recovered = await grain.RecoverAsync();
        Assert.Equal(kind == StorageFailureKind.UnknownOutcome ? 1 : 0, recovered.Delivered);
        Assert.Equal(0, recovered.Pending);
    }

    [Fact]
    public async Task Failed_recovery_after_an_accessor_error_warns_with_the_reminder_failure_and_unknown_count()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnDeactivation, StorageFailureKind.UnknownOutcome);
        fixture.Reminders.RejectWrites(grain.GetGrainId());

        await grain.FailWithBrokenAccessorAsync(StorageFailureKind.UnknownOutcome);
        await WaitForDeactivationAsync(grain);

        Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
        var warning = Assert.Single(fixture.Logs.Warnings, entry =>
            Equals(entry.Properties.GetValueOrDefault("GrainId"), grain.GetGrainId()));
        Assert.Equal("OutboxDeactivationReminderFailed", warning.EventId.Name);
        Assert.Null(warning.Properties["OutboxItemCount"]);
        Assert.IsNotType<FormatException>(warning.Exception);
    }

    [Fact]
    public async Task An_unfenced_accessor_error_remains_a_warning_without_registering_a_reminder()
    {
        var grain = await CreateGrainAsync(OutboxReminderPolicy.OnDeactivation, StorageFailureKind.UnknownOutcome);

        await grain.FailWithBrokenAccessorAsync(null);
        await WaitForDeactivationAsync(grain);

        Assert.Equal(new ReminderApiCounts(0, 0, 0), fixture.ReminderApi.For(grain.GetGrainId()));
        var warning = Assert.Single(fixture.Logs.Warnings, entry =>
            Equals(entry.Properties.GetValueOrDefault("GrainId"), grain.GetGrainId()));
        Assert.IsType<FormatException>(warning.Exception);
        Assert.Null(warning.Properties["OutboxItemCount"]);
    }

    private async Task<IFencedOutboxGrain> CreateGrainAsync(OutboxReminderPolicy policy, StorageFailureKind kind)
    {
        var grain = fixture.GetUniqueGrain<IFencedOutboxGrain>();
        await grain.ConfigureAsync(policy, kind == StorageFailureKind.DidNotPersist);
        await grain.ObserveAsync();
        if (policy == OutboxReminderPolicy.KeepRegistered)
        {
            await fixture.ReminderApi.WaitForRegistrationAsync(grain.GetGrainId())
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        return grain;
    }

    private Task WaitForDeactivationAsync(IFencedOutboxGrain grain) =>
        fixture.Deactivations.WaitForCompletionAsync(grain.GetGrainId())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

    private void AssertNoWarnings(IFencedOutboxGrain grain) =>
        Assert.DoesNotContain(fixture.Logs.Warnings, entry =>
            Equals(entry.Properties.GetValueOrDefault("GrainId"), grain.GetGrainId()));
}
