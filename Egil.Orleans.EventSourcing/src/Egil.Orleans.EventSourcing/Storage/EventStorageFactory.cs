using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.Storage;

/// <summary>
/// Factory for creating strongly-typed event storage instances.
/// Supports Orleans-style attribute-based dependency injection with keyed services.
/// </summary>
public interface IEventStorageFactory
{
    /// <summary>
    /// Creates a strongly-typed event storage instance for the specified configuration name.
    /// </summary>
    IEventStorage<TEvent, TOutboxEvent> Create<TEvent, TOutboxEvent>(
        string storageName,
        IGrainContext grainContext)
        where TEvent : class
        where TOutboxEvent : class;
}

/// <summary>
/// Default implementation of IEventStorageFactory using keyed services.
/// </summary>
public sealed class EventStorageFactory : IEventStorageFactory
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILoggerFactory loggerFactory;

    /// <summary>
    /// Initializes a new instance of EventStorageFactory.
    /// </summary>
    public EventStorageFactory(IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <summary>
    /// Creates a strongly-typed event storage instance for the specified configuration name.
    /// </summary>
    public IEventStorage<TEvent, TOutboxEvent> Create<TEvent, TOutboxEvent>(
        string storageName,
        IGrainContext grainContext)
        where TEvent : class
        where TOutboxEvent : class
    {
        // Get the named configuration from keyed services
        var configuration = serviceProvider.GetRequiredKeyedService<EventStorageConfiguration<TEvent, TOutboxEvent>>(storageName);
        var logger = loggerFactory.CreateLogger<TypedEventStorage<TEvent, TOutboxEvent>>();
        
        return new TypedEventStorage<TEvent, TOutboxEvent>(configuration, grainContext, logger);
    }
}

/// <summary>
/// Extension methods for configuring event storage in DI.
/// </summary>
public static class EventStorageServiceCollectionExtensions
{
    /// <summary>
    /// Adds event storage factory to the service collection.
    /// </summary>
    public static IServiceCollection AddEventStorageFactory(this IServiceCollection services)
    {
        services.AddSingleton<IEventStorageFactory, EventStorageFactory>();
        return services;
    }

    /// <summary>
    /// Adds a named event storage configuration to the service collection.
    /// Similar to Orleans' AddAzureTableGrainStorage pattern.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type</typeparam>
    /// <typeparam name="TOutboxEvent">The outbox event type</typeparam>
    /// <param name="services">The service collection</param>
    /// <param name="name">The configuration name</param>
    /// <param name="configureOptions">Configuration delegate</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddEventStorageConfiguration<TEvent, TOutboxEvent>(
        this IServiceCollection services,
        string name,
        Action<EventStorageConfigurationBuilder<TEvent, TOutboxEvent>> configureOptions)
        where TEvent : class
        where TOutboxEvent : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var builder = new EventStorageConfigurationBuilder<TEvent, TOutboxEvent>(services);
        configureOptions(builder);

        var configuration = builder.Build();
        services.AddKeyedSingleton(name, configuration);

        return services;
    }

    /// <summary>
    /// Registers a typed event storage instance using the [EventStorage] attribute pattern.
    /// This enables Orleans-style dependency injection for event storage.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type</typeparam>
    /// <typeparam name="TOutboxEvent">The outbox event type</typeparam>
    /// <param name="services">The service collection</param>
    /// <param name="name">The storage configuration name</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddTypedEventStorage<TEvent, TOutboxEvent>(
        this IServiceCollection services,
        string name)
        where TEvent : class
        where TOutboxEvent : class
    {
        // Register a factory that creates typed event storage instances
        services.AddKeyedTransient<IEventStorage<TEvent, TOutboxEvent>>(
            name,
            (serviceProvider, key) =>
            {
                var factory = serviceProvider.GetRequiredService<IEventStorageFactory>();
                // Note: IGrainContext will be resolved at runtime by Orleans
                var grainContext = serviceProvider.GetRequiredService<IGrainContext>();
                return factory.Create<TEvent, TOutboxEvent>((string)key!, grainContext);
            });

        return services;
    }
}

/// <summary>
/// Builder for configuring event storage.
/// </summary>
/// <typeparam name="TEvent">The domain event type</typeparam>
/// <typeparam name="TOutboxEvent">The outbox event type</typeparam>
public sealed class EventStorageConfigurationBuilder<TEvent, TOutboxEvent>
    where TEvent : class
    where TOutboxEvent : class
{
    private readonly IServiceCollection services;
    private IEventSerializer<TEvent>? eventSerializer;
    private IEventSerializer<TOutboxEvent>? outboxEventSerializer;
    private IEventStorageProvider? storageProvider;
    private IEventRetentionPolicy? retentionPolicy;
    private DeduplicationStrategy deduplicationStrategy = DeduplicationStrategy.None;

    internal EventStorageConfigurationBuilder(IServiceCollection services)
    {
        this.services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Configures the event serializer for domain events.
    /// </summary>
    public EventStorageConfigurationBuilder<TEvent, TOutboxEvent> UseEventSerializer(IEventSerializer<TEvent> serializer)
    {
        eventSerializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        return this;
    }

    /// <summary>
    /// Configures the event serializer for outbox events.
    /// </summary>
    public EventStorageConfigurationBuilder<TEvent, TOutboxEvent> UseOutboxEventSerializer(IEventSerializer<TOutboxEvent> serializer)
    {
        outboxEventSerializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        return this;
    }

    /// <summary>
    /// Configures the storage provider.
    /// </summary>
    public EventStorageConfigurationBuilder<TEvent, TOutboxEvent> UseStorageProvider(IEventStorageProvider provider)
    {
        storageProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        return this;
    }

    /// <summary>
    /// Configures the retention policy.
    /// </summary>
    public EventStorageConfigurationBuilder<TEvent, TOutboxEvent> UseRetentionPolicy(IEventRetentionPolicy policy)
    {
        retentionPolicy = policy;
        return this;
    }

    /// <summary>
    /// Configures the deduplication strategy.
    /// </summary>
    public EventStorageConfigurationBuilder<TEvent, TOutboxEvent> UseDeduplicationStrategy(DeduplicationStrategy strategy)
    {
        deduplicationStrategy = strategy;
        return this;
    }

    /// <summary>
    /// Builds the configuration.
    /// </summary>
    internal EventStorageConfiguration<TEvent, TOutboxEvent> Build()
    {
        return new EventStorageConfiguration<TEvent, TOutboxEvent>
        {
            EventSerializer = eventSerializer ?? throw new InvalidOperationException("Event serializer not configured"),
            OutboxEventSerializer = outboxEventSerializer ?? throw new InvalidOperationException("Outbox event serializer not configured"),
            StorageProvider = storageProvider ?? throw new InvalidOperationException("Storage provider not configured"),
            RetentionPolicy = retentionPolicy,
            DeduplicationStrategy = deduplicationStrategy
        };
    }
}

/// <summary>
/// Extension methods for configuring event storage in DI.
/// </summary>
public static class EventStorageServiceCollectionExtensions
{
    /// <summary>
    /// Adds event storage factory to the service collection.
    /// </summary>
    public static IServiceCollection AddEventStorageFactory(this IServiceCollection services)
    {
        services.AddSingleton<IEventStorageFactory, EventStorageFactory>();
        return services;
    }

    /// <summary>
    /// Adds a named event storage configuration.
    /// </summary>
    public static IServiceCollection AddEventStorageConfiguration<TEvent, TOutboxEvent>(
        this IServiceCollection services,
        string name,
        Action<EventStorageConfigurationBuilder<TEvent, TOutboxEvent>> configure)
        where TEvent : class
        where TOutboxEvent : class
    {
        var builder = new EventStorageConfigurationBuilder<TEvent, TOutboxEvent>(services, name);
        configure(builder);
        
        var configuration = builder.Build();
        services.AddKeyedSingleton<EventStorageConfiguration<TEvent, TOutboxEvent>>(name, configuration);
        
        return services;
    }
}

/// <summary>
/// Builder for event storage configurations.
/// </summary>
/// <typeparam name="TEvent">The base event type</typeparam>
/// <typeparam name="TOutboxEvent">The base outbox event type</typeparam>
public sealed class EventStorageConfigurationBuilder<TEvent, TOutboxEvent>
    where TEvent : class
    where TOutboxEvent : class
{
    private readonly IServiceCollection services;
    private readonly string name;
    private IEventSerializer<TEvent>? eventSerializer;
    private IEventSerializer<TOutboxEvent>? outboxEventSerializer;
    private IEventStorageProvider? storageProvider;
    private object? tableClientConfiguration;

    /// <summary>
    /// Initializes a new instance of EventStorageConfigurationBuilder.
    /// </summary>
    public EventStorageConfigurationBuilder(IServiceCollection services, string name)
    {
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        this.name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Configures the event serializer.
    /// </summary>
    public EventStorageConfigurationBuilder<TEvent, TOutboxEvent> UseEventSerializer(IEventSerializer<TEvent> serializer)
    {
        eventSerializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        return this;
    }

    /// <summary>
    /// Configures the outbox event serializer.
    /// </summary>
    public EventStorageConfigurationBuilder<TEvent, TOutboxEvent> UseOutboxEventSerializer(IEventSerializer<TOutboxEvent> serializer)
    {
        outboxEventSerializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        return this;
    }

    /// <summary>
    /// Configures the storage provider.
    /// </summary>
    public EventStorageConfigurationBuilder<TEvent, TOutboxEvent> UseStorageProvider(IEventStorageProvider provider)
    {
        storageProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        return this;
    }

    /// <summary>
    /// Configures the storage provider using a factory.
    /// </summary>
    public EventStorageConfigurationBuilder<TEvent, TOutboxEvent> UseStorageProvider<TProvider>()
        where TProvider : class, IEventStorageProvider
    {
        services.AddSingleton<TProvider>();
        // The storage provider will be resolved when the configuration is built
        return this;
    }

    /// <summary>
    /// Configures table client settings.
    /// </summary>
    public EventStorageConfigurationBuilder<TEvent, TOutboxEvent> UseTableClientConfiguration(object configuration)
    {
        tableClientConfiguration = configuration;
        return this;
    }

    /// <summary>
    /// Uses System.Text.Json for event serialization.
    /// </summary>
    public EventStorageConfigurationBuilder<TEvent, TOutboxEvent> UseSystemTextJsonEventSerializer(
        System.Text.Json.JsonSerializerOptions? options = null)
    {
        eventSerializer = new Serialization.SystemTextJsonEventSerializer<TEvent>(options);
        return this;
    }

    /// <summary>
    /// Uses System.Text.Json for outbox event serialization.
    /// </summary>
    public EventStorageConfigurationBuilder<TEvent, TOutboxEvent> UseSystemTextJsonOutboxEventSerializer(
        System.Text.Json.JsonSerializerOptions? options = null)
    {
        outboxEventSerializer = new Serialization.SystemTextJsonEventSerializer<TOutboxEvent>(options);
        return this;
    }

    /// <summary>
    /// Uses System.Text.Json for both event and outbox event serialization.
    /// </summary>
    public EventStorageConfigurationBuilder<TEvent, TOutboxEvent> UseSystemTextJson(
        System.Text.Json.JsonSerializerOptions? options = null)
    {
        UseSystemTextJsonEventSerializer(options);
        UseSystemTextJsonOutboxEventSerializer(options);
        return this;
    }

    /// <summary>
    /// Builds the configuration.
    /// </summary>
    internal EventStorageConfiguration<TEvent, TOutboxEvent> Build()
    {
        if (eventSerializer == null)
            throw new InvalidOperationException("Event serializer must be configured");
        
        if (outboxEventSerializer == null)
            throw new InvalidOperationException("Outbox event serializer must be configured");
        
        if (storageProvider == null)
            throw new InvalidOperationException("Storage provider must be configured");

        return new EventStorageConfiguration<TEvent, TOutboxEvent>
        {
            Name = name,
            EventSerializer = eventSerializer,
            OutboxEventSerializer = outboxEventSerializer,
            StorageProvider = storageProvider,
            TableClientConfiguration = tableClientConfiguration
        };
    }
}
