using Microsoft.Extensions.DependencyInjection;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Extensions for configuring event sourcing in Orleans.
/// </summary>
public static class EventSourcingExtensions
{
    /// <summary>
    /// Adds event sourcing services to the service collection.
    /// </summary>
    public static IServiceCollection AddEventSourcing(this IServiceCollection services, Action<EventSourcingOptions> configure)
    {
        var options = new EventSourcingOptions();
        configure(options);
        
        services.AddSingleton(options);
        
        if (options.EventStorageFactory != null)
            services.AddSingleton(options.EventStorageFactory);
        
        if (options.OutboxStorageFactory != null)
            services.AddSingleton(options.OutboxStorageFactory);
            
        if (options.EventPublisherFactory != null)
            services.AddSingleton(options.EventPublisherFactory);

        return services;
    }
}

/// <summary>
/// Configuration options for event sourcing.
/// </summary>
public sealed class EventSourcingOptions
{
    /// <summary>
    /// Factory for creating event storage instances.
    /// </summary>
    public Func<IServiceProvider, IEventStorage>? EventStorageFactory { get; set; }
    
    /// <summary>
    /// Factory for creating outbox storage instances.
    /// </summary>
    public Func<IServiceProvider, IOutboxStorage>? OutboxStorageFactory { get; set; }
    
    /// <summary>
    /// Factory for creating event publisher instances.
    /// </summary>
    public Func<IServiceProvider, IEventPublisher>? EventPublisherFactory { get; set; }
    
    /// <summary>
    /// Interval for processing outbox events.
    /// </summary>
    public TimeSpan OutboxProcessingInterval { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Maximum retry count for outbox events.
    /// </summary>
    public int MaxOutboxRetries { get; set; } = 5;
}
