using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Egil.Orleans.EventSourcing.AzureStorage;

internal static class Telemetry<TEvent>
{
    internal static readonly ActivitySource ActivitySource = new ActivitySource("Egil.Orleans.EventSourcing");
    internal static readonly Meter Meter = new Meter("Egil.Orleans.EventSourcing");
    internal static readonly Counter<long> AppendEventsCounter = Meter.CreateCounter<long>("egil-orleans-eventsourcing-events-append", description: "The number of events appended to the event stream.");
    internal static readonly Counter<long> ReadEventsCounter = Meter.CreateCounter<long>("egil-orleans-eventsourcing-events-read", description: "The number of events read from the event stream.");
    internal static readonly Histogram<double> AppendEventsDurationHistogram = Meter.CreateHistogram<double>("egil-orleans-eventsourcing-events-append-duration", description: "The duration of AppendEventsAsync in milliseconds");
    internal static readonly Histogram<double> ReadEventsDurationHistogram = Meter.CreateHistogram<double>("egil-orleans-eventsourcing-events-read-duration", description: "The duration of ReadEventsAsync in milliseconds");
    internal static readonly string EventTypeName = typeof(TEvent).Name;
    internal static readonly KeyValuePair<string, object?> EventTypeTag = new KeyValuePair<string, object?>("EventType", EventTypeName);

    internal static Activity? StartActivity(string methodName)
        => ActivitySource.StartActivity($"{Telemetry<TEvent>.EventTypeName}.{methodName}", ActivityKind.Client);
}
