namespace Egil.Orleans.EventSourcing.Internal;

/// <summary>
/// Internal service responsible for loading and saving projections from/to storage.
/// </summary>
internal interface IProjectionLoader<TProjection> where TProjection : class
{
    /// <summary>
    /// Loads a projection from storage for the specified grain.
    /// If no projection exists or loading fails, returns null.
    /// </summary>
    ValueTask<TProjection?> LoadAsync(string grainId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a projection to storage for the specified grain.
    /// </summary>
    ValueTask SaveAsync(string grainId, TProjection projection, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of projection loader that delegates to IEventStorage.
/// </summary>
internal class ProjectionLoader<TProjection> : IProjectionLoader<TProjection>
    where TProjection : class, IEventProjection<TProjection>
{
    private readonly IEventStorage eventStorage;

    public ProjectionLoader(IEventStorage eventStorage)
    {
        this.eventStorage = eventStorage ?? throw new ArgumentNullException(nameof(eventStorage));
    }

    public ValueTask<TProjection?> LoadAsync(string grainId, CancellationToken cancellationToken = default)
    {
        return eventStorage.LoadProjectionAsync<TProjection>(grainId, cancellationToken);
    }

    public ValueTask SaveAsync(string grainId, TProjection projection, CancellationToken cancellationToken = default)
    {
        // Note: Projection saving will be handled through the SaveAsync method
        // when events are processed. This method is kept for potential future direct projection saves.
        throw new NotSupportedException("Direct projection saves are not supported. Use ProcessEventsAsync for atomic event and projection saves.");
    }
}
