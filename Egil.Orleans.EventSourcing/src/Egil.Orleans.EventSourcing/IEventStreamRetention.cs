using Egil.Orleans.EventSourcing.Storage;

namespace Egil.Orleans.EventSourcing;

public interface IEventStreamRetention
{
    /// <summary>
    /// When true, an <see cref="EventEntry{TEvent}"/> where all
    /// <see cref="ReactorState.Status"/> are <see cref="ReactorOperationStatus.CompleteSuccessful"/>
    /// will be deleted.
    /// </summary>
    bool UntilProcessed { get; init; }

    /// <summary>
    /// Keeps only the latest event by <see cref="EventEntry{TEvent}.EventId"/>
    /// and <see cref="EventEntry{TEvent}.Timestamp"/>, if true.
    /// </summary>
    bool LatestDistinct { get; init; }

    /// <summary>
    /// Keeps the latest <see name="Count"/> events, if not null.
    /// </summary>
    int? Count { get; init; }

    /// <summary>
    /// Keeps the events until their <see cref="EventEntry{TEvent}.Timestamp"/> is
    /// older than <see cref="MaxAge"/>, if not null.
    /// </summary>
    TimeSpan? MaxAge { get; init; }
}
