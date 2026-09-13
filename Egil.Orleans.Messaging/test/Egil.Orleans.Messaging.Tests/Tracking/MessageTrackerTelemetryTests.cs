using System.Diagnostics.Metrics;
using Egil.Orleans.Messaging.Tests.Streams;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Tests.Tracking;

public sealed class MessageTrackerTelemetryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sender_clock_skew_never_exports_negative_receive_lag(bool streamMessage)
    {
        var source = Guid.NewGuid().ToString("N");
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(clock);
        var metricName = streamMessage ? "stream.message.receive.lag" : "outbox.message.receive.lag";
        var sourceTag = streamMessage ? "stream.namespace" : "sender.grain.type";
        List<double> measurements = [];
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "egil.orleans.messaging" && instrument.Name == metricName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == sourceTag && Equals(tag.Value, source))
                {
                    measurements.Add(value);
                }
            }
        });
        listener.Start();
        var future = clock.GetUtcNow().AddMinutes(5);

        var accepted = streamMessage
            ? tracker.TryAcceptMessage(new StreamCursor(source, new DiagnosticToken(null, future)), out _)
            : tracker.TryAcceptMessage(new OutboxSequenceToken(1, GrainId.Create(source, "one"), future, future), out _);

        Assert.True(accepted);
        Assert.Equal(0d, Assert.Single(measurements));
    }
}
