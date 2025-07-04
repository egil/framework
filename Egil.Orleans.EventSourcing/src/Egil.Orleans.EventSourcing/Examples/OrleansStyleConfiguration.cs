using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Egil.Orleans.EventSourcing.Serialization;
using Egil.Orleans.EventSourcing.Storage;
using Egil.Orleans.EventSourcing.Examples;
using System.Text.Json;

namespace Egil.Orleans.EventSourcing.Examples;

/// <summary>
/// Example configuration showing how to set up Orleans-style event storage
/// with attribute-based dependency injection, similar to IPersistentState pattern.
/// </summary>
public static class OrleansStyleEventStorageConfiguration
{
    /// <summary>
    /// Configures Orleans silo with event sourcing using the new attribute-based dependency injection pattern.
    /// </summary>
    public static ISiloBuilder ConfigureEventSourcing(this ISiloBuilder siloBuilder)
    {
        var services = siloBuilder.Services;

        // 1. Add the event storage factory (required for [EventStorage] attribute)
        services.AddEventStorageFactory();

        // 2. Configure specific event storage configurations using keyed services
        ConfigureUserEventStorage(services);
        ConfigureUserEventStorageWithSharedScope(services);

        // 3. Register typed event storage instances for dependency injection
        services.AddTypedEventStorage<UserEvent, UserOutboxEvent>("user-events");
        services.AddTypedEventStorage<UserEvent, UserOutboxEvent>("user-events-shared");

        // 4. Add outbox postman services (optional)
        services.AddOutboxPostmanService();

        return siloBuilder;
    }

    /// <summary>
    /// Configures event storage for user events with Orleans persistent state.
    /// </summary>
    private static void ConfigureUserEventStorage(IServiceCollection services)
    {
        services.AddEventStorageConfiguration<UserEvent, UserOutboxEvent>(
            "user-events",
            builder => builder
                .UseEventSerializer(new SystemTextJsonEventSerializer<UserEvent>(CreateJsonOptions()))
                .UseOutboxEventSerializer(new SystemTextJsonEventSerializer<UserOutboxEvent>(CreateJsonOptions()))
                .UseStorageProvider(CreateAzureTableEventStorageProvider())
                .UseDeduplicationStrategy(DeduplicationStrategy.EventId));
    }

    /// <summary>
    /// Configures event storage for user events with shared transaction scope.
    /// </summary>
    private static void ConfigureUserEventStorageWithSharedScope(IServiceCollection services)
    {
        services.AddEventStorageConfiguration<UserEvent, UserOutboxEvent>(
            "user-events-shared",
            builder => builder
                .UseEventSerializer(new SystemTextJsonEventSerializer<UserEvent>(CreateJsonOptions()))
                .UseOutboxEventSerializer(new SystemTextJsonEventSerializer<UserOutboxEvent>(CreateJsonOptions()))
                .UseStorageProvider(CreateSharedScopeStorageProvider())
                .UseRetentionPolicy(new TimeBasedRetentionPolicy(TimeSpan.FromDays(90)))
                .UseDeduplicationStrategy(DeduplicationStrategy.ContentHash));
    }

    /// <summary>
    /// Creates JSON serialization options optimized for event sourcing.
    /// </summary>
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        
        // Configure polymorphic serialization for event hierarchies
        options.TypeInfoResolver = JsonSerializer.IsReflectionEnabledByDefault 
            ? new PolymorphicTypeResolver<UserEvent>()
            : throw new InvalidOperationException("Reflection-based serialization is not enabled");

        // Add converters for specific types if needed
        // options.Converters.Add(new DateTimeOffsetConverter());

        return options;
    }

    /// <summary>
    /// Creates an Azure Table Storage provider for events.
    /// </summary>
    private static IEventStorageProvider CreateAzureTableEventStorageProvider()
    {
        // In a real implementation, this would create an actual Azure Table provider
        // For now, return a mock provider for demonstration
        return new MockEventStorageProvider("azure-table");
    }

    /// <summary>
    /// Creates a shared scope storage provider that stores projections with events.
    /// </summary>
    private static IEventStorageProvider CreateSharedScopeStorageProvider()
    {
        // In a real implementation, this would create a provider that supports IProjectionStorage
        return new MockSharedScopeStorageProvider("shared-scope");
    }
}

/// <summary>
/// Example polymorphic type resolver for event hierarchies.
/// </summary>
public class PolymorphicTypeResolver<TBaseEvent> : DefaultJsonTypeInfoResolver
    where TBaseEvent : class
{
    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var jsonTypeInfo = base.GetTypeInfo(type, options);

        // Configure polymorphic serialization for event types
        if (jsonTypeInfo.Type == typeof(TBaseEvent))
        {
            jsonTypeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$type",
                IgnoreUnrecognizedTypeDiscriminators = false,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization
            };

            // Add derived types - in a real application, this could be done via reflection
            if (typeof(TBaseEvent) == typeof(UserEvent))
            {
                jsonTypeInfo.PolymorphismOptions.DerivedTypes.Add(
                    new JsonDerivedType(typeof(UserCreated), "UserCreated"));
                jsonTypeInfo.PolymorphismOptions.DerivedTypes.Add(
                    new JsonDerivedType(typeof(UserEmailChanged), "UserEmailChanged"));
                jsonTypeInfo.PolymorphismOptions.DerivedTypes.Add(
                    new JsonDerivedType(typeof(UserDeactivated), "UserDeactivated"));
            }
        }

        return jsonTypeInfo;
    }
}

/// <summary>
/// Mock event storage provider for demonstration purposes.
/// </summary>
public class MockEventStorageProvider : IEventStorageProvider
{
    private readonly string name;

    public MockEventStorageProvider(string name)
    {
        this.name = name;
    }

    public IEventStorage<TEvent> Create<TEvent>(Orleans.Runtime.IGrainContext grainContext)
    {
        throw new NotImplementedException($"Mock storage provider '{name}' - replace with actual implementation");
    }
}

/// <summary>
/// Mock shared scope storage provider that supports projection storage.
/// </summary>
public class MockSharedScopeStorageProvider : IEventStorageProvider
{
    private readonly string name;

    public MockSharedScopeStorageProvider(string name)
    {
        this.name = name;
    }

    public IEventStorage<TEvent> Create<TEvent>(Orleans.Runtime.IGrainContext grainContext)
    {
        throw new NotImplementedException($"Mock shared scope storage provider '{name}' - replace with actual implementation");
    }
}

/// <summary>
/// Time-based retention policy for events.
/// </summary>
public class TimeBasedRetentionPolicy : IEventRetentionPolicy
{
    private readonly TimeSpan retentionPeriod;

    public TimeBasedRetentionPolicy(TimeSpan retentionPeriod)
    {
        this.retentionPeriod = retentionPeriod;
    }

    public bool ShouldRetain(DateTimeOffset eventTimestamp, long sequenceNumber)
    {
        return DateTimeOffset.UtcNow - eventTimestamp <= retentionPeriod;
    }
}

/// <summary>
/// Extension methods for configuring outbox postman services.
/// </summary>
public static class OutboxPostmanServiceExtensions
{
    /// <summary>
    /// Adds outbox postman services to the service collection.
    /// </summary>
    public static IServiceCollection AddOutboxPostmanService(this IServiceCollection services)
    {
        services.AddSingleton<OutboxPostmanService>();
        
        // Configure outbox postman for user outbox events
        services.AddOutboxPostman<UserOutboxEvent>(builder => builder
            .UseConfiguration(new OutboxPostmanConfiguration
            {
                BatchSize = 10,
                ProcessingInterval = TimeSpan.FromSeconds(5),
                RetryPolicy = new ExponentialBackoffRetryPolicy(3, TimeSpan.FromMilliseconds(500))
            }));

        return services;
    }
}

/// <summary>
/// Example of using the configured event sourcing in a host application.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var hostBuilder = Host.CreateDefaultBuilder(args)
            .UseOrleans(siloBuilder =>
            {
                siloBuilder
                    .UseLocalhostClustering()
                    .ConfigureServices(services =>
                    {
                        // Add any additional services needed
                    })
                    .ConfigureEventSourcing(); // <-- Use our new configuration
            });

        var host = hostBuilder.Build();
        await host.RunAsync();
    }
}
