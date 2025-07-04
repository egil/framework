using Orleans;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Base interface for event-sourced grains.
/// Provides the common contract for grains that process events.
/// </summary>
public interface IEventGrain : IGrainWithStringKey
{
    /// <summary>
    /// Processes an event in the grain.
    /// </summary>
    /// <param name="event">The event to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ProcessEventAsync(object @event, CancellationToken cancellationToken = default);
}