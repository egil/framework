using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Egil.Orleans.EventSourcing.Storage;
using Egil.Orleans.EventSourcing.Serialization;

namespace Orleans.Hosting;

/// <summary>
/// Extension methods for configuring Orleans-style event sourcing with attribute-based dependency injection.
/// </summary>
public static class OrleansEventSourcingHostingExtensions
{
    /// <summary>
    /// Adds Orleans-style event sourcing to the silo with attribute-based dependency injection.
    /// This enables the [EventStorage("name")] pattern similar to [PersistentState("name")].
    /// </summary>
    /// <param name="siloBuilder">The silo builder</param>
    /// <returns>The silo builder for chaining</returns>
    public static ISiloBuilder AddOrleansEventSourcing(this ISiloBuilder siloBuilder)
    {
        var services = siloBuilder.Services;

        // Add the event storage factory (required for [EventStorage] attribute resolution)
        services.AddEventStorageFactory();

        // Add default outbox postman service
        services.AddSingleton<OutboxPostmanService>();

        return siloBuilder;
    }

    /// <summary>
    /// Adds a named event storage configuration with type safety.
    /// This method should be called for each event/outbox event type combination you want to configure.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type</typeparam>
    /// <typeparam name="TOutboxEvent">The outbox event type</typeparam>
    /// <param name="siloBuilder">The silo builder</param>
    /// <param name="name">The configuration name used in [EventStorage("name")]</param>
    /// <param name="configure">Configuration delegate</param>
    /// <returns>The silo builder for chaining</returns>
    public static ISiloBuilder AddEventStorage<TEvent, TOutboxEvent>(
        this ISiloBuilder siloBuilder,
        string name,
        Action<EventStorageConfigurationBuilder<TEvent, TOutboxEvent>> configure)
        where TEvent : class
        where TOutboxEvent : class
    {
        // Add the storage configuration
        siloBuilder.Services.AddEventStorageConfiguration(name, configure);

        // Register the typed event storage for dependency injection
        siloBuilder.Services.AddTypedEventStorage<TEvent, TOutboxEvent>(name);

        return siloBuilder;
    }

    /// <summary>
    /// Adds Azure Table Storage event storage with default configuration.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type</typeparam>
    /// <typeparam name="TOutboxEvent">The outbox event type</typeparam>
    /// <param name="siloBuilder">The silo builder</param>
    /// <param name="name">The configuration name</param>
    /// <param name="connectionString">Azure Storage connection string</param>
    /// <param name="configure">Optional additional configuration</param>
    /// <returns>The silo builder for chaining</returns>
    public static ISiloBuilder AddAzureTableEventStorage<TEvent, TOutboxEvent>(
        this ISiloBuilder siloBuilder,
        string name,
        string connectionString,
        Action<EventStorageConfigurationBuilder<TEvent, TOutboxEvent>>? configure = null)
        where TEvent : class
        where TOutboxEvent : class
    {
        return siloBuilder.AddEventStorage<TEvent, TOutboxEvent>(name, builder =>
        {
            // Configure with Azure Table Storage
            builder
                .UseEventSerializer(CreateSystemTextJsonSerializer<TEvent>())
                .UseOutboxEventSerializer(CreateSystemTextJsonSerializer<TOutboxEvent>())
                .UseStorageProvider(new AzureTableEventStorageProvider(connectionString))
                .UseDeduplicationStrategy(DeduplicationStrategy.EventId);

            // Apply additional configuration if provided
            configure?.Invoke(builder);
        });
    }

    /// <summary>
    /// Adds Azure Blob Storage event storage with default configuration.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type</typeparam>
    /// <typeparam name="TOutboxEvent">The outbox event type</typeparam>
    /// <param name="siloBuilder">The silo builder</param>
    /// <param name="name">The configuration name</param>
    /// <param name="connectionString">Azure Storage connection string</param>
    /// <param name="configure">Optional additional configuration</param>
    /// <returns>The silo builder for chaining</returns>
    public static ISiloBuilder AddAzureBlobEventStorage<TEvent, TOutboxEvent>(
        this ISiloBuilder siloBuilder,
        string name,
        string connectionString,
        Action<EventStorageConfigurationBuilder<TEvent, TOutboxEvent>>? configure = null)
        where TEvent : class
        where TOutboxEvent : class
    {
        return siloBuilder.AddEventStorage<TEvent, TOutboxEvent>(name, builder =>
        {
            // Configure with Azure Blob Storage
            builder
                .UseEventSerializer(CreateSystemTextJsonSerializer<TEvent>())
                .UseOutboxEventSerializer(CreateSystemTextJsonSerializer<TOutboxEvent>())
                .UseStorageProvider(new AzureBlobEventStorageProvider(connectionString))
                .UseDeduplicationStrategy(DeduplicationStrategy.ContentHash);

            // Apply additional configuration if provided
            configure?.Invoke(builder);
        });
    }

    /// <summary>
    /// Adds in-memory event storage for testing and development.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type</typeparam>
    /// <typeparam name="TOutboxEvent">The outbox event type</typeparam>
    /// <param name="siloBuilder">The silo builder</param>
    /// <param name="name">The configuration name</param>
    /// <param name="configure">Optional additional configuration</param>
    /// <returns>The silo builder for chaining</returns>
    public static ISiloBuilder AddInMemoryEventStorage<TEvent, TOutboxEvent>(
        this ISiloBuilder siloBuilder,
        string name,
        Action<EventStorageConfigurationBuilder<TEvent, TOutboxEvent>>? configure = null)
        where TEvent : class
        where TOutboxEvent : class
    {
        return siloBuilder.AddEventStorage<TEvent, TOutboxEvent>(name, builder =>
        {
            // Configure with in-memory storage
            builder
                .UseEventSerializer(CreateSystemTextJsonSerializer<TEvent>())
                .UseOutboxEventSerializer(CreateSystemTextJsonSerializer<TOutboxEvent>())
                .UseStorageProvider(new InMemoryEventStorageProvider())
                .UseDeduplicationStrategy(DeduplicationStrategy.None);

            // Apply additional configuration if provided
            configure?.Invoke(builder);
        });
    }

    /// <summary>
    /// Creates a System.Text.Json serializer with optimized settings for event sourcing.
    /// </summary>
    private static IEventSerializer<T> CreateSystemTextJsonSerializer<T>()
        where T : class
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        return new SystemTextJsonEventSerializer<T>(options);
    }
}

// Placeholder implementations for the example
internal class AzureTableEventStorageProvider : IEventStorageProvider
{
    private readonly string connectionString;

    public AzureTableEventStorageProvider(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public IEventStorage<TEvent> Create<TEvent>(Orleans.Runtime.IGrainContext grainContext)
    {
        throw new NotImplementedException("Replace with actual Azure Table Storage implementation");
    }
}

internal class AzureBlobEventStorageProvider : IEventStorageProvider
{
    private readonly string connectionString;

    public AzureBlobEventStorageProvider(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public IEventStorage<TEvent> Create<TEvent>(Orleans.Runtime.IGrainContext grainContext)
    {
        throw new NotImplementedException("Replace with actual Azure Blob Storage implementation");
    }
}

internal class InMemoryEventStorageProvider : IEventStorageProvider
{
    public IEventStorage<TEvent> Create<TEvent>(Orleans.Runtime.IGrainContext grainContext)
    {
        throw new NotImplementedException("Replace with actual in-memory implementation");
    }
}
