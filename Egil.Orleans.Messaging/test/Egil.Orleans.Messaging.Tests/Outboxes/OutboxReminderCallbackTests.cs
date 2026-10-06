namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxReminderCallbackTests
{
    [Fact]
    public async Task Cancellation_overload_reaches_the_custom_reminder_handler()
    {
        var receiver = new CustomReminderReceiver();

        await ((IRemindable)receiver).ReceiveReminder("maintenance", default, TestContext.Current.CancellationToken);

        Assert.Equal("maintenance", receiver.ReceivedName);
    }

    // The legacy override is the documented IOutboxGrain escape hatch. The new
    // framework overload must forward to it instead of invoking OM's default route.
    private sealed class CustomReminderReceiver : IOutboxGrain
    {
        public string? ReceivedName { get; private set; }

        public Task ReceiveReminder(string reminderName, TickStatus status)
        {
            ReceivedName = reminderName;
            return Task.CompletedTask;
        }
    }
}


