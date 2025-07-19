namespace Egil.Orleans.EventSourcing.Storage;

internal class EventStreamRetention<TEvent> : IEventStreamRetention
    where TEvent : notnull
{
    public required bool UntilProcessed { get; init; } = false;

    public required int? Count { get; init; } = null;

    public required TimeSpan? MaxAge { get; init; } = null;

    public bool LatestDistinct { get; init; } = false;

    public required Func<TEvent, DateTimeOffset>? TimestampSelector { get; init; } = null;

    public required Func<TEvent, string>? EventIdSelector { get; init; } = null;
}