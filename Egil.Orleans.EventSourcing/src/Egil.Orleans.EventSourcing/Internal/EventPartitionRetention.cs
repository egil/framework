namespace Egil.Orleans.EventSourcing.Internal;

internal class EventPartitionRetention<TEvent>
    where TEvent : notnull
{
    public required bool UntilProcessed { get; init; } = false;

    public required int? Count { get; init; } = null;

    public required TimeSpan? MaxAge { get; init; } = null;

    public required Func<TEvent, DateTimeOffset>? TimestampSelector { get; init; } = null;

    public required Func<TEvent, string>? KeySelector { get; init; } = null;
}