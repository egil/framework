# Functional API Design for Orleans Event Sourcing

## Core Principles

The redesigned Orleans Event Sourcing library follows these principles:
- **Functional Programming**: Pure functions for event application with immutable projections
- **Async-First**: All event handlers support async operations and dependency injection
- **Strong Typing**: Generic, type-safe APIs avoiding `object` types wherever possible
- **Separation of Concerns**: Event stream configuration is separate from event handling logic
- **Builder Pattern**: Fluent, discoverable API for configuring streams and handlers
- **Performance**: ValueTask for better async performance and minimal allocations
- **Flexibility**: Multiple ways to define handlers (classes, delegates, DI-based)
- **Mutable Outbox**: Outbox pattern with mutable accumulator for side effects

## Key Types

- `EventHandlerDelegate<TEvent, TProjection>` - Async delegate for adhoc event handlers
- `IEventHandler<TEvent, TProjection>` - Async interface for class-based event handlers
- `EventHandler<TEvent, TProjection>` - Async base class for event handlers with helper methods
- `EventOutbox` - Mutable outbox for accumulating events during processing
- `EventStreamConfiguration` - Configuration for event streams (deduplication, retention, etc.)
- `EventStreamBuilder<TProjection>` - Fluent builder for configuring streams and handlers
- `EventGrain<TProjection>` - Base class for event-sourced grains
- `EventProcessingResult<TProjection>` - Result type for event processing operations

## Handler Definition Options

### 1. Class-Based Handlers (Async)
```csharp
public sealed class UserRegisteredHandler : EventHandler<UserRegistered, UserProjection>
{
    public override async ValueTask<UserProjection> ApplyEventAsync(UserRegistered @event, UserProjection projection, EventOutbox outbox)
    {
        // Can perform async operations like database calls, API calls, etc.
        await SomeAsyncOperation();
        
        var updatedProjection = projection with
        {
            UserId = @event.UserId,
            Email = @event.Email,
            LastUpdated = @event.Timestamp,
            EventCount = projection.EventCount + 1
        };
        
        // Add outbox events using the mutable outbox
        outbox.Publish(new WelcomeEmailRequested(@event.UserId, @event.Email), @event.UserId, "email-notifications");
        outbox.Publish(new UserAnalyticsEvent(@event.UserId, "UserRegistered", @event.Timestamp), @event.UserId, "analytics");
        
        return updatedProjection;
    }
}
```

### 2. Delegate-Based Handlers (Async)
```csharp
// Simple async projection update
EventHandlerDelegate<UserStatusUpdated, UserProjection> statusHandler = 
    async (evt, projection, outbox) => 
    {
        await Task.Delay(0); // Example async operation
        
        outbox.Publish(new UserAnalyticsEvent(evt.UserId, "StatusUpdated", evt.Timestamp), evt.UserId, "analytics");
        
        return projection with 
        { 
            Status = evt.Status, 
            LastUpdated = evt.Timestamp,
            EventCount = projection.EventCount + 1
        };
    };

// Complex async handler with outbox
EventHandlerDelegate<UserRegistered, UserProjection> registeredHandler = 
    async (evt, projection, outbox) =>
    {
        // Async dependency injection or external service calls
        await ValidateUserRegistrationAsync(evt.Email);
        
        var updated = projection with { UserId = evt.UserId, Email = evt.Email };
        
        // Accumulate outbox events
        outbox.Publish(new WelcomeEmailRequested(evt.UserId, evt.Email), evt.UserId, "email-notifications");
        outbox.Publish(new UserAnalyticsEvent(evt.UserId, "UserRegistered", evt.Timestamp), evt.UserId, "analytics");
        
        return updated;
    };
```

### 3. Service Provider-Based Handlers (DI Support)
```csharp
// Handler registered in DI container with async operations
builder
    .ForEvent<UserEmailChanged>()
    .ToStream("user-profile")
    .HandledBy<UserEmailChangedHandler>(Services); // Uses DI to resolve handler
```

## Grain Configuration Examples

### Mixed Handler Types
```csharp
protected override void ConfigureEventStreams(EventStreamBuilder<UserProjection> builder)
{
    // Class-based handler
    builder
        .ForEvent<UserRegistered>()
        .ToStream("user-lifecycle")
        .WithDeduplication(evt => evt.UserId)
        .WithRetention(EventRetentionPolicies.KeepLatestPerDeduplicationKey())
        .HandledBy(new UserRegisteredHandler());

    // Service provider-based handler
    builder
        .ForEvent<UserEmailChanged>()
        .ToStream("user-profile")
        .WithDeduplication(evt => evt.UserId)
        .HandledBy<UserEmailChangedHandler>(Services);

    // Inline delegate handler
    builder
        .ForEvent<UserStatusUpdated>()
        .ToStream("user-status")
        .WithRetention(EventRetentionPolicies.KeepRecent(TimeSpan.FromDays(30)))
        .HandledBy(async (evt, projection, outbox) => 
        {
            // Can perform async operations
            await Task.Delay(0);
            
            outbox.Publish(new UserAnalyticsEvent(evt.UserId, "StatusUpdated", evt.Timestamp), evt.UserId, "analytics");
            
            return projection with
            {
                Status = evt.Status,
                LastUpdated = evt.Timestamp,
                EventCount = projection.EventCount + 1
            };
        });
}
```

## Performance Features

### ValueTask for Async Performance
```csharp
// Uses ValueTask<T> for better async performance
public delegate ValueTask<TProjection> EventHandlerDelegate<in TEvent, TProjection>(
    TEvent @event, 
    TProjection projection, 
    EventOutbox outbox);

public interface IEventHandler<in TEvent, TProjection>
{
    ValueTask<TProjection> ApplyEventAsync(TEvent @event, TProjection projection, EventOutbox outbox);
}
```

### Mutable Outbox Pattern
```csharp
// Mutable outbox for efficient accumulation during event processing
public sealed class EventOutbox
{
    public void Publish(object @event, string grainId, string? targetStream = null);
    public void Add(OutboxEvent outboxEvent);
    public void AddRange(IEnumerable<OutboxEvent> outboxEvents);
    public Outbox ToImmutable(); // Convert to immutable for storage
}
```

### Result Types
```csharp
// Result of processing an event
public readonly record struct EventProcessingResult<TProjection>
{
    public TProjection Projection { get; }
    public long SequenceNumber { get; }
    public Outbox Outbox { get; }
}
```

## Architecture Benefits

1. **Type Safety**: Compile-time verification with no `object` casting
2. **Async Support**: Full async/await support throughout the API
3. **Dependency Injection**: Seamless integration with DI containers
4. **Performance**: ValueTask usage and mutable outbox for efficiency
5. **Flexibility**: Multiple handler definition patterns for different scenarios
6. **Testability**: Pure async functions are easy to unit test
7. **Clear Separation**: Configuration and business logic are cleanly separated
8. **Orleans Integration**: Both persistent state and table storage support
## Usage Examples

### 1. Strongly-Typed Async Event Handlers
```csharp
// Pure async functional event handlers with DI support
public sealed class UserRegisteredHandler : EventHandler<UserRegistered, UserProjection>
{
    private readonly IUserService userService;
    
    public UserRegisteredHandler(IUserService userService)
    {
        this.userService = userService;
    }
    
    public override async ValueTask<UserProjection> ApplyEventAsync(UserRegistered @event, UserProjection projection, EventOutbox outbox)
    {
        // Can perform async operations with injected dependencies
        await userService.ValidateUserAsync(@event.Email);
        
        var updatedProjection = projection with
        {
            UserId = @event.UserId,
            Email = @event.Email,
            LastUpdated = @event.Timestamp,
            EventCount = projection.EventCount + 1
        };
        
        // Add outbox events using mutable outbox
        outbox.Publish(new WelcomeEmailRequested(@event.UserId, @event.Email), @event.UserId, "email-notifications");
        outbox.Publish(new UserAnalyticsEvent(@event.UserId, "UserRegistered", @event.Timestamp), @event.UserId, "analytics");
        
        return updatedProjection;
    }
}
```

### 2. Flexible Event Handler Options
You can use class-based handlers or inline async functional handlers:

```csharp
// Class-based approach with DI
builder
    .ForEvent<UserRegistered>()
    .ToStream("user-lifecycle")
    .WithDeduplication(evt => evt.UserId)
    .HandledBy<UserRegisteredHandler>(Services); // Resolves from DI

// Async functional approach with outbox
builder
    .ForEvent<UserRegistered>()
    .ToStream("user-lifecycle") 
    .HandledBy(async (evt, projection, outbox) => 
    {
        // Can perform async operations
        await SomeAsyncOperation();
        
        var updated = projection with { UserId = evt.UserId, Email = evt.Email };
        
        // Accumulate outbox events
        outbox.Publish(new WelcomeEmailRequested(evt.UserId, evt.Email), evt.UserId, "email-notifications");
        
        return updated;
    });

// Simple async functional approach (projection only)
builder
    .ForEvent<UserEmailChanged>()
    .ToStream("user-profile")
    .HandledBy(async (evt, projection, outbox) =>
    {
        await Task.Delay(0); // Example async operation
        
        outbox.Publish(new UserAnalyticsEvent(evt.UserId, "EmailChanged", evt.Timestamp), evt.UserId, "analytics");
        
        return projection with { Email = evt.NewEmail, LastUpdated = evt.Timestamp };
    });
```

### 3. Generic Base Classes
- `EventGrain<TProjection>` - Base grain class parameterized by projection type
- `EventHandler<TEvent, TProjection>` - Async base handler class for specific event types
- `EventStreamBuilder<TProjection>` - Fluent configuration builder
- `ProjectionState<TProjection>` - Wrapper for persisted projection data
- `EventOutbox` - Mutable outbox for accumulating side effects

### 4. Compositional Design
Grains compose multiple async event handlers through builder configuration:

```csharp
protected override void ConfigureEventStreams(EventStreamBuilder<UserProjection> builder)
{
    builder
        .ForEvent<UserRegistered>()
        .ToStream("user-lifecycle")
        .WithDeduplication(evt => evt.UserId)
        .WithRetention(EventRetentionPolicies.KeepLatestPerDeduplicationKey())
        .HandledBy<UserRegisteredHandler>(Services); // DI-based

    builder
        .ForEvent<UserEmailChanged>()
        .ToStream("user-profile")
        .WithDeduplication(evt => evt.UserId)
        .HandledBy(new UserEmailChangedHandler()); // Instance-based

    builder
        .ForEvent<UserStatusUpdated>()
        .ToStream("user-status")
        .WithRetention(EventRetentionPolicies.KeepRecent(TimeSpan.FromDays(30)))
        .HandledBy(async (evt, projection, outbox) => // Async delegate-based
        {
            await Task.Delay(0); // Example async operation
            
            outbox.Publish(new UserAnalyticsEvent(evt.UserId, "StatusUpdated", evt.Timestamp), evt.UserId, "analytics");
            
            return projection with
            {
                Status = evt.Status,
                LastUpdated = evt.Timestamp,
                EventCount = projection.EventCount + 1
            };
        });
}
```

## Benefits of This Approach

### 1. Predictability
- Pure async functions are easy to test and reason about
- Clear async control flow with ValueTask performance
- Explicit side effects through mutable outbox pattern

### 2. Type Safety
- Compile-time verification of event-to-handler mappings
- Generic constraints ensure type compatibility
- No casting or reflection in event application

### 3. Composability
- Async event handlers can be reused across different grain types
- Retention policies are composable and reusable
- Easy to add new event types without modifying existing code

### 4. Testability
```csharp
[Test]
public async Task ApplyEventAsync_UserRegistered_UpdatesProjectionAndCreatesOutboxEvents()
{
    // Arrange
    var handler = new UserRegisteredHandler();
    var projection = UserProjection.Empty;
    var @event = new UserRegistered("user1", "test@example.com", DateTime.UtcNow);
    var outbox = new EventOutbox();
    
    // Act
    var result = await handler.ApplyEventAsync(@event, projection, outbox);
    
    // Assert
    Assert.That(result.UserId, Is.EqualTo("user1"));
    Assert.That(result.Email, Is.EqualTo("test@example.com"));
    Assert.That(outbox.Events, Has.Count.EqualTo(2));
}
```

### 5. Functional Composition
- Async handlers can be composed to create complex behavior
- Easy to pipe async transformations
- Natural fit for functional programming patterns with async support

## Usage Patterns

### 1. Pure Event Sourcing (No Streams)
```csharp
public sealed class UserGrain : EventGrain<UserProjection>, IUserGrain
{
    // Only processes events via direct RPC calls
    public async Task RegisterUser(string email)
    {
        var @event = new UserRegistered(this.GetPrimaryKeyString(), email, DateTime.UtcNow);
        await ProcessEventAsync(@event);
    }
}
```

### 2. Event Sourcing with Orleans Streams
```csharp
// Event sourcing with Orleans stream subscription would be handled
// separately from the core EventGrain<T> to maintain separation of concerns.
// The core event sourcing logic remains focused on state management,
// while stream subscription is handled by separate stream subscriber grains.
```

### 3. Dependency Injection Integration
```csharp
public sealed class UserEmailChangedHandler : EventHandler<UserEmailChanged, UserProjection>
{
    private readonly IEmailService emailService;
    private readonly ILogger<UserEmailChangedHandler> logger;
    
    public UserEmailChangedHandler(IEmailService emailService, ILogger<UserEmailChangedHandler> logger)
    {
        this.emailService = emailService;
        this.logger = logger;
    }
    
    public override async ValueTask<UserProjection> ApplyEventAsync(UserEmailChanged @event, UserProjection projection, EventOutbox outbox)
    {
        // Async dependency injection usage
        await emailService.ValidateEmailAsync(@event.NewEmail);
        logger.LogInformation("Email changed for user {UserId}", @event.UserId);
        
        outbox.Publish(new UserAnalyticsEvent(@event.UserId, "EmailChanged", @event.Timestamp), @event.UserId, "analytics");
        
        return projection with
        {
            Email = @event.NewEmail,
            LastUpdated = @event.Timestamp,
            EventCount = projection.EventCount + 1
        };
```

### 4. Table Storage Projection Support
```csharp
// EventGrain without IPersistentState uses shared transaction scope
public sealed class TableStorageUserGrain : EventGrain<UserProjection>, IUserGrain
{
    // Constructor without IPersistentState - projection stored with events in table storage
    public TableStorageUserGrain(
        IEventStorage eventStorage,
        IOutboxStorage outboxStorage,
        IEventPublisher eventPublisher,
        ILogger<TableStorageUserGrain> logger)
        : base(eventStorage, outboxStorage, eventPublisher, logger)
    {
    }

    public async Task RegisterUser(string email)
    {
        var @event = new UserRegistered(this.GetPrimaryKeyString(), email, DateTime.UtcNow);
        await ProcessEventAsync(@event);
    }

    protected override void ConfigureEventStreams(EventStreamBuilder<UserProjection> builder)
    {
        builder
            .ForEvent<UserRegistered>()
            .ToStream("user-lifecycle")
            .WithDeduplication(evt => evt.UserId)
            .HandledBy<UserRegisteredHandler>(Services);
    }

    protected override UserProjection CreateInitialProjection() => UserProjection.Empty;

    public override async Task ProcessEventAsync(object @event, CancellationToken cancellationToken = default)
    {
        var result = await ProcessEventWithHandlerAsync(@event, cancellationToken);
        // The base class handles persistence to table storage with transactional scope
    }
}
```

## Key Features Summary

### Async-First Design
- All event handlers use `ValueTask<TProjection>` for optimal async performance
- Full async/await support throughout the API
- Supports async dependency injection and external service calls

### Mutable Outbox Pattern
- `EventOutbox` provides a mutable accumulator for side effects during event processing
- Clean separation between projection updates and outbox events
- Efficient accumulation without intermediate allocations

### Dual Storage Support
- **Orleans Persistent State**: Traditional Orleans state management
- **Table Storage**: Shared transaction scope with events for ACID guarantees

### Dependency Injection Integration
- Event handlers can receive dependencies through constructor injection
- Service provider-based handler registration: `HandledBy<THandler>(Services)`
- Supports complex async operations with injected services

### Type Safety and Performance
- Strongly-typed generic APIs eliminate runtime casting
- `ValueTask<T>` usage for better async performance
- Immutable projections with record types

### Flexible Handler Registration
- **Class-based**: `HandledBy(new Handler())` or `HandledBy<Handler>(Services)`
- **Delegate-based**: `HandledBy(async (evt, proj, outbox) => { ... })`
- **Instance-based**: Direct handler instances
- **DI-based**: Resolve from service provider

This design provides a clean, async-first, and strongly-typed approach to event sourcing in Orleans while supporting both traditional Orleans state and shared transaction scope storage patterns.

## Projection Migration

Handle projection schema evolution with pure functions:

```csharp
protected override UserProjection MigrateProjection(UserProjection oldProjection, int fromVersion, int toVersion)
{
    return (fromVersion, toVersion) switch
    {
        (1, 2) => oldProjection with { /* add new properties with defaults */ },
        (2, 3) => oldProjection with { /* modify structure */ },
        _ => oldProjection // No migration needed
    };
}
```

## Retention Policies

Configure event retention per stream:

```csharp
builder
    .ForEvent<UserRegistered>()
    .ToStream("user-lifecycle")
    .WithRetention(EventRetentionPolicies.KeepLatestPerDeduplicationKey())
    .HandledBy<UserRegisteredHandler>(Services);

builder
    .ForEvent<UserStatusUpdated>()
    .ToStream("user-status")
    .WithRetention(EventRetentionPolicies.KeepRecent(TimeSpan.FromDays(30)))
    .HandledBy(async (evt, proj, outbox) => /* handler logic */);
```
        _ => oldProjection
    };
}
```

## Next Steps

1. Implement the base EventGrain logic using this functional API
2. Create Azure Table Storage implementations for IEventStorage and IOutboxStorage  
3. Add concrete IEventPublisher implementations
4. Build integration tests demonstrating the full workflow

The design provides a clean, functional API that makes event sourcing natural and composable while maintaining Orleans best practices.

## Outbox Postman System

The outbox postman system handles publishing outbox events to external systems:

### 1. Postman Interfaces
```csharp
// Interface for handling specific outbox event types
public interface IOutboxPostman<in TOutboxEvent>
{
    ValueTask<bool> ProcessEventAsync(TOutboxEvent outboxEvent, CancellationToken cancellationToken = default);
}

// Base class for postmen
public abstract class OutboxPostman<TOutboxEvent> : IOutboxPostman<TOutboxEvent>
{
    public abstract ValueTask<bool> ProcessEventAsync(TOutboxEvent outboxEvent, CancellationToken cancellationToken = default);
}
```

### 2. Example Postmen
```csharp
public sealed class EmailNotificationPostman : OutboxPostman<WelcomeEmailRequested>
{
    private readonly IEmailService emailService;
    
    public EmailNotificationPostman(IEmailService emailService)
    {
        this.emailService = emailService;
    }

    public override async ValueTask<bool> ProcessEventAsync(WelcomeEmailRequested outboxEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            await emailService.SendWelcomeEmailAsync(outboxEvent.UserId, outboxEvent.Email, cancellationToken);
            return true; // Successfully processed
        }
        catch
        {
            return false; // Will be retried
        }
    }
}
```

### 3. Configuration
```csharp
public static OutboxPostmanConfiguration ConfigurePostmen(IServiceProvider serviceProvider)
{
    return new OutboxPostmanConfiguration(serviceProvider)
        .RegisterPostman<WelcomeEmailRequested, EmailNotificationPostman>()
        .RegisterPostman<UserAnalyticsEvent, AnalyticsPostman>()
        .RegisterPostman<UserStatusUpdated>(async (statusEvent, ct) =>
        {
            // Simple delegate-based postman
            Console.WriteLine($"User {statusEvent.UserId} status changed to {statusEvent.Status}");
            return true;
        });
}
```

## Key Improvements

### 1. Better Method Naming
- `outbox.Add()` instead of `outbox.Publish()` - makes it clear events are added to outbox, not immediately published

### 2. Projection Safety
- `IEventProjection<TSelf>` interface ensures projections can always be initialized safely
- Static factory method `CreateDefault()` eliminates null reference issues

### 3. Flexible Handler Support
- **With Outbox**: `IEventHandler<TEvent, TProjection>` and `EventHandlerDelegate<TEvent, TProjection>`
- **Read-Only**: `IEventHandlerReadOnly<TEvent, TProjection>` and `EventHandlerDelegateReadOnly<TEvent, TProjection>`
- Both support async operations and dependency injection

### 4. Simplified DI Integration
- Handlers registered via `HandledBy<THandler>()` without needing to pass IServiceProvider
- EventGrain automatically provides service provider to builder

### 5. Unified Storage
- `IEventStorage` now handles both events and outbox storage
- Eliminates need for separate `IOutboxStorage` interface
- Supports both Orleans persistent state and shared transaction scope

### 6. Outbox Processing
- Dedicated postman system for handling outbox events
- Type-safe registration of postmen for specific event types
- Automatic retry handling for failed outbox events

This design provides a clean, async-first, strongly-typed, and flexible approach to event sourcing in Orleans while supporting both traditional Orleans state and shared transaction scope storage patterns, with comprehensive outbox support for side effects.
