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
/// Default implementation of projection loader.
/// </summary>
internal class ProjectionLoader<TProjection> : IProjectionLoader<TProjection> 
    where TProjection : class, IEventProjection<TProjection>
{
    private readonly IEventStorage _eventStorage;

    public ProjectionLoader(IEventStorage eventStorage)
    {
        _eventStorage = eventStorage ?? throw new ArgumentNullException(nameof(eventStorage));
    }

    public ValueTask<TProjection?> LoadAsync(string grainId, CancellationToken cancellationToken = default)
    {
        // For now, return null to simulate empty storage
        // This will be implemented when we add storage functionality
        return ValueTask.FromResult<TProjection?>(null);
    }

    public ValueTask SaveAsync(string grainId, TProjection projection, CancellationToken cancellationToken = default)
    {
        // For now, do nothing
        // This will be implemented when we add storage functionality
        return ValueTask.CompletedTask;
    }
}
