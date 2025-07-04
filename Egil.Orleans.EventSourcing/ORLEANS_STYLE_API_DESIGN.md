# Orleans-Style Event Sourcing API Design

This document outlines the design for a functional, immutable, and strongly-typed event sourcing API for Orleans (.NET 9) that follows Orleans' IPersistentState pattern with attribute-based dependency injection.

## Key Design Principles

1. **Orleans-Style Dependency Injection**: Uses `[EventStorage("name")]` attributes similar to `[PersistentState("name")]`
2. **Immutable and Functional**: All projections and events are immutable records
3. **Strongly-Typed**: Generic type safety for events, outbox events, and projections
4. **Async and DI-Friendly**: All handlers are async and support dependency injection
5. **Configurable Storage**: Event serialization and storage are separated from business logic using keyed services
6. **Multi-Stream Support**: Each grain can have multiple event streams with different configurations
7. **Outbox Pattern**: Built-in support for outbox events with configurable processing

## Core Components

### 1. Orleans-Style Event Storage Injection

The new API follows Orleans' familiar pattern of attribute-based dependency injection, making it feel native to Orleans developers:

```csharp
public class UserGrain : EventGrain<UserProjection, UserEvent, UserOutboxEvent>
{
    public UserGrain(
        [PersistentState("projection")] IPersistentState<ProjectionState<UserProjection>> projectionState,
        [EventStorage("user-events")] IEventStorage<UserEvent, UserOutboxEvent> eventStorage,
        ILogger<UserGrain> logger)
        : base(projectionState, eventStorage, logger)
    {
    }
}
```

### 2. Shared Transaction Scope Support

For scenarios where you want projection and events stored together:

```csharp
public class UserGrain : EventGrain<UserProjection, UserEvent, UserOutboxEvent>
{
    public UserGrain(
        [EventStorage("user-events-shared")] IEventStorage<UserEvent, UserOutboxEvent> eventStorage,
        ILogger<UserGrain> logger)
        : base(eventStorage, logger) // No Orleans persistent state - projection stored with events
    {
    }
}
```

### 3. Configuration with Keyed Services

Event storage configurations are registered using Orleans' keyed services pattern:

```csharp
services.AddEventStorageConfiguration<UserEvent, UserOutboxEvent>(
    "user-events",
    builder => builder
        .UseEventSerializer(new SystemTextJsonEventSerializer<UserEvent>(jsonOptions))
        .UseOutboxEventSerializer(new SystemTextJsonEventSerializer<UserOutboxEvent>(jsonOptions))
        .UseStorageProvider(azureTableProvider)
        .UseDeduplicationStrategy(DeduplicationStrategy.EventId));

// Register typed event storage for dependency injection
services.AddTypedEventStorage<UserEvent, UserOutboxEvent>("user-events");
```

## Event Storage Architecture

### Type-Safe Event Storage Interface

```csharp
public interface IEventStorage<TEvent, TOutboxEvent> : IEventStorage
    where TEvent : class
    where TOutboxEvent : class
{
    ValueTask<AppendEventsResult> AppendEventsAsync(
        IEnumerable<TEvent> events, 
        CancellationToken cancellationToken = default);

    ValueTask<AppendEventsResult> AppendOutboxEventsAsync(
        IEnumerable<TOutboxEvent> events, 
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<StoredEvent<TEvent>> ReadEventsAsync(
        long fromSequenceNumber = 0, 
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<StoredEvent<TOutboxEvent>> ReadOutboxEventsAsync(
        long fromSequenceNumber = 0, 
        CancellationToken cancellationToken = default);

    // For shared transaction scope scenarios
    ValueTask<ProjectionState<TProjection>?> ReadProjectionAsync<TProjection>(
        CancellationToken cancellationToken = default)
        where TProjection : notnull, IEventProjection<TProjection>;

    ValueTask WriteProjectionAsync<TProjection>(
        ProjectionState<TProjection> projectionState,
        CancellationToken cancellationToken = default)
        where TProjection : notnull, IEventProjection<TProjection>;
}
```

### Configurable Storage Provider

```csharp
public sealed class EventStorageConfiguration<TEvent, TOutboxEvent>
{
    public required IEventSerializer<TEvent> EventSerializer { get; init; }
    public required IEventSerializer<TOutboxEvent> OutboxEventSerializer { get; init; }
    public required IEventStorageProvider StorageProvider { get; init; }
    public IEventRetentionPolicy? RetentionPolicy { get; init; }
    public DeduplicationStrategy DeduplicationStrategy { get; init; }
}

public enum DeduplicationStrategy
{
    None,
    EventId,
    ContentHash
}
```

## Event Processing with Stream Builders

### Async, Strongly-Typed Event Handlers

```csharp
protected override void ConfigureEventStreams(EventStreamBuilder<UserProjection> builder)
{
    builder
        .ForStream("domain-events")
        .OnEvent<UserCreated>()
            .HandleAsync(async (evt, projection, outbox) =>
            {
                var updatedProjection = projection with
                {
                    UserId = evt.UserId,
                    Name = evt.Name,
                    Email = evt.Email,
                    IsActive = true,
                    CreatedAt = evt.Timestamp,
                    LastModifiedAt = evt.Timestamp
                };

                // Add outbox event for notification
                outbox.Add(new UserNotificationRequested(
                    Guid.NewGuid().ToString(),
                    evt.UserId,
                    $"Welcome {evt.Name}!",
                    evt.Timestamp));

                return updatedProjection;
            })
        .OnEvent<UserEmailChanged>()
            .HandleAsync(async (evt, projection, outbox) =>
            {
                var updatedProjection = projection with
                {
                    Email = evt.NewEmail,
                    LastModifiedAt = evt.Timestamp
                };

                outbox.Add(new UserEmailUpdateRequested(
                    Guid.NewGuid().ToString(),
                    evt.UserId,
                    evt.NewEmail,
                    evt.Timestamp));

                return updatedProjection;
            });
}
```

## Immutable Event and Projection Types

### Event Hierarchy

```csharp
public abstract record UserEvent(string UserId, DateTimeOffset Timestamp);

public sealed record UserCreated(string UserId, string Name, string Email, DateTimeOffset Timestamp) 
    : UserEvent(UserId, Timestamp);

public sealed record UserEmailChanged(string UserId, string OldEmail, string NewEmail, DateTimeOffset Timestamp) 
    : UserEvent(UserId, Timestamp);
```

### Outbox Events

```csharp
public abstract record UserOutboxEvent(string EventId, DateTimeOffset Timestamp);

public sealed record UserNotificationRequested(string EventId, string UserId, string Message, DateTimeOffset Timestamp) 
    : UserOutboxEvent(EventId, Timestamp);
```

### Projection with Default Factory

```csharp
public sealed record UserProjection(
    string UserId,
    string Name,
    string Email,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt) : IEventProjection<UserProjection>
{
    public static UserProjection CreateDefault() => new(
        string.Empty,
        string.Empty, 
        string.Empty,
        false,
        DateTimeOffset.MinValue,
        DateTimeOffset.MinValue);
}
```

## Serialization and Storage Configuration

### JSON Serialization with Polymorphic Support

```csharp
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
jsonOptions.TypeInfoResolver = new PolymorphicTypeResolver<UserEvent>();

public class PolymorphicTypeResolver<TBaseEvent> : DefaultJsonTypeInfoResolver
{
    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var jsonTypeInfo = base.GetTypeInfo(type, options);

        if (jsonTypeInfo.Type == typeof(TBaseEvent))
        {
            jsonTypeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$type",
                IgnoreUnrecognizedTypeDiscriminators = false,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization
            };

            // Add derived types
            jsonTypeInfo.PolymorphismOptions.DerivedTypes.Add(
                new JsonDerivedType(typeof(UserCreated), "UserCreated"));
            jsonTypeInfo.PolymorphismOptions.DerivedTypes.Add(
                new JsonDerivedType(typeof(UserEmailChanged), "UserEmailChanged"));
        }

        return jsonTypeInfo;
    }
}
```

## Outbox Pattern Integration

### Type-Safe Outbox Processing

```csharp
services.AddOutboxPostman<UserOutboxEvent>(builder => builder
    .UseConfiguration(new OutboxPostmanConfiguration
    {
        BatchSize = 10,
        ProcessingInterval = TimeSpan.FromSeconds(5),
        RetryPolicy = new ExponentialBackoffRetryPolicy(3, TimeSpan.FromMilliseconds(500))
    }));
```

## Storage Provider Options

### 1. Orleans Persistent State + Event Storage
- Projection stored in Orleans persistent state
- Events stored in configurable event storage
- Best for existing Orleans applications

### 2. Shared Transaction Scope
- Both projection and events stored in same storage system
- Enables atomic transactions
- Best for new applications with ACID requirements

## Complete Configuration Example

### Silo Configuration

```csharp
public static ISiloBuilder ConfigureEventSourcing(this ISiloBuilder siloBuilder)
{
    var services = siloBuilder.Services;

    // 1. Add the event storage factory (required for [EventStorage] attribute)
    services.AddEventStorageFactory();

    // 2. Configure specific event storage configurations using keyed services
    services.AddEventStorageConfiguration<UserEvent, UserOutboxEvent>(
        "user-events",
        builder => builder
            .UseEventSerializer(new SystemTextJsonEventSerializer<UserEvent>(CreateJsonOptions()))
            .UseOutboxEventSerializer(new SystemTextJsonEventSerializer<UserOutboxEvent>(CreateJsonOptions()))
            .UseStorageProvider(CreateAzureTableEventStorageProvider())
            .UseDeduplicationStrategy(DeduplicationStrategy.EventId));

    // 3. Register typed event storage instances for dependency injection
    services.AddTypedEventStorage<UserEvent, UserOutboxEvent>("user-events");

    // 4. Add outbox postman services (optional)
    services.AddOutboxPostmanService();

    return siloBuilder;
}
```

### Host Application

```csharp
public class Program
{
    public static async Task Main(string[] args)
    {
        var hostBuilder = Host.CreateDefaultBuilder(args)
            .UseOrleans(siloBuilder =>
            {
                siloBuilder
                    .UseLocalhostClustering()
                    .ConfigureEventSourcing(); // <-- Use our new configuration
            });

        var host = hostBuilder.Build();
        await host.RunAsync();
    }
}
```

## Benefits of This Design

1. **Orleans Integration**: Seamless integration with Orleans using familiar patterns
2. **Type Safety**: Compile-time type checking for events, projections, and configurations
3. **Separation of Concerns**: Business logic separated from storage and serialization
4. **Testability**: Easy to mock and test with dependency injection
5. **Performance**: Async/await throughout, optimized for high-throughput scenarios
6. **Flexibility**: Support for multiple storage providers and serialization formats
7. **Migration Path**: Can coexist with existing Orleans grains and gradually migrate

## Migration Strategy

1. **Phase 1**: Implement new grains using Orleans-style event storage
2. **Phase 2**: Migrate existing grains to use new pattern
3. **Phase 3**: Deprecate legacy event storage interfaces
4. **Phase 4**: Remove legacy code after full migration

This design provides a modern, functional approach to event sourcing in Orleans while maintaining familiar patterns and excellent performance characteristics.

## Comparison with Legacy API

### Legacy Pattern (Complex, Tightly Coupled)
```csharp
// Old way - tightly coupled configuration
public class UserGrain : EventSourcedGrain<UserEvent, UserProjection>
{
    protected override void ConfigureEventStreams(IEventStreamBuilder builder)
    {
        builder.ConfigureStream("events", config =>
        {
            config.WithDeduplication(DeduplicationStrategy.EventId);
            config.WithRetention(TimeSpan.FromDays(90));
            config.WithHandler<UserCreated>(ApplyUserCreated);
        });
    }

    private UserProjection ApplyUserCreated(UserEvent evt, UserProjection projection)
    {
        // Limited to synchronous operations
        return projection with { /* ... */ };
    }
}
```

### New Orleans-Style Pattern (Clean, Decoupled)
```csharp
// New way - Orleans-style attribute injection
public class UserGrain : EventGrain<UserProjection, UserEvent, UserOutboxEvent>
{
    public UserGrain(
        [PersistentState("projection")] IPersistentState<ProjectionState<UserProjection>> projectionState,
        [EventStorage("user-events")] IEventStorage<UserEvent, UserOutboxEvent> eventStorage,
        ILogger<UserGrain> logger)
        : base(projectionState, eventStorage, logger)
    {
    }

    protected override void ConfigureEventStreams(EventStreamBuilder<UserProjection> builder)
    {
        builder
            .ForStream("domain-events")
            .OnEvent<UserCreated>()
            .HandleAsync(async (evt, projection, outbox) =>
            {
                // Full async support with outbox pattern
                await SomeAsyncOperation();
                outbox.Add(new NotificationRequested(evt.UserId));
                return projection with { /* ... */ };
            });
    }
}
```

The new pattern provides better separation of concerns, full async support, and follows Orleans' established conventions.
