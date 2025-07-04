# Updated Event Sourcing API Design

This document describes the improvements made to the event sourcing API based on the requested changes.

## Summary of Changes

### 1. Generic EventStreamConfiguration

**Before:**
```csharp
public sealed class EventStreamConfiguration
{
    public Func<object, bool> ShouldStoreEvent { get; init; } = _ => true;
    public Func<object, string?> GetDeduplicationId { get; init; } = _ => null;
}
```

**After:**
```csharp
public sealed class EventStreamConfiguration<TEvent>
    where TEvent : class
{
    public Func<TEvent, bool> ShouldStoreEvent { get; init; } = _ => true;
    public Func<TEvent, string?> GetDeduplicationId { get; init; } = _ => null;
}
```

**Benefits:**
- Type safety: No more casting from `object`
- Better IntelliSense and compile-time checking
- Clear indication of what event types the configuration applies to

### 2. Generic EventOutbox with Orleans Streams Support

**Before:**
```csharp
public sealed class EventOutbox
{
    public void Add(object @event, string grainId, string? targetStream = null);
    public Outbox ToImmutable();
}
```

**After:**
```csharp
public sealed class EventOutbox<TOutboxEvent>
    where TOutboxEvent : class
{
    // Regular outbox events
    public void Add(TOutboxEvent @event, string grainId, string? targetStream = null);
    
    // Orleans Streams support
    public void SendToStream(TOutboxEvent @event, string streamNamespace, string streamKey);
    
    // Custom async actions
    public void AddCustomAction(Func<CancellationToken, ValueTask> action, string? description = null);
    
    public Outbox<TOutboxEvent> ToImmutable();
}
```

**New Supporting Types:**
```csharp
public sealed record OrleansStreamTarget<TEvent>(
    TEvent Event,
    string StreamNamespace,
    string StreamKey)
    where TEvent : class;

public sealed record CustomOutboxAction(
    Func<CancellationToken, ValueTask> Action,
    string? Description = null);

public sealed record Outbox<TOutboxEvent>(
    IReadOnlyList<OutboxEvent> Events,
    IReadOnlyList<OrleansStreamTarget<TOutboxEvent>> StreamTargets,
    IReadOnlyList<CustomOutboxAction> CustomActions)
    where TOutboxEvent : class;
```

**Benefits:**
- Native Orleans Streams integration
- Support for custom async operations in outbox
- Type-safe outbox event handling
- Clear separation of different outbox event types

### 3. Simplified Event Handler Interfaces

**Before:**
```csharp
// Multiple interfaces and delegates
public interface IEventHandler<in TEvent, TProjection> { ... }
public interface IEventHandlerReadOnly<in TEvent, TProjection> { ... }
public delegate ValueTask<TProjection> EventHandlerDelegate<in TEvent, TProjection>(...);
public delegate ValueTask<TProjection> EventHandlerDelegateReadOnly<in TEvent, TProjection>(...);

// Abstract base classes
public abstract class EventHandler<TEvent, TProjection> { ... }
public abstract class EventHandlerReadOnly<TEvent, TProjection> { ... }
```

**After:**
```csharp
// Single interface for all scenarios
public interface IEventHandler<in TEvent, TProjection, TOutboxEvent>
    where TProjection : notnull
    where TOutboxEvent : class
{
    ValueTask<TProjection> ApplyEventAsync(
        TEvent @event, 
        TProjection projection, 
        EventOutbox<TOutboxEvent>? outbox = null);
}

// Single delegate for all scenarios
public delegate ValueTask<TProjection> EventHandlerDelegate<in TEvent, TProjection, TOutboxEvent>(
    TEvent @event, 
    TProjection projection, 
    EventOutbox<TOutboxEvent>? outbox = null)
    where TProjection : notnull
    where TOutboxEvent : class;
```

**Benefits:**
- Simplified API surface - single interface instead of multiple
- Optional outbox parameter supports both read-only and side-effect handlers
- No need for abstract base classes
- Consistent naming and patterns

### 4. [FromKeyedServices] Dependency Injection

**Before:**
```csharp
public UserGrain(
    [EventStorage("user-events")] IEventStorage<UserEvent, UserOutboxEvent> eventStorage)
```

**After:**
```csharp
public UserGrain(
    [FromKeyedServices("user-events")] IEventStorage<UserEvent, UserOutboxEvent> eventStorage)
```

**Benefits:**
- Uses standard .NET dependency injection patterns
- Better integration with Microsoft.Extensions.DependencyInjection
- Follows established conventions in Orleans and .NET ecosystem
- No custom attribute required

## Usage Examples

### Basic Event Handler

```csharp
public class UserCreatedHandler : IEventHandler<UserCreated, UserProjection, UserOutboxEvent>
{
    public ValueTask<UserProjection> ApplyEventAsync(
        UserCreated @event, 
        UserProjection projection, 
        EventOutbox<UserOutboxEvent>? outbox = null)
    {
        // Update projection
        var updatedProjection = projection with
        {
            UserId = @event.UserId,
            Name = @event.Name,
            Email = @event.Email
        };

        // Add side effects if outbox is provided
        if (outbox is not null)
        {
            // Regular outbox event
            outbox.Add(new WelcomeEmailRequested(@event.UserId), @event.UserId);
            
            // Send to Orleans stream
            outbox.SendToStream(new UserCreated(@event.UserId), "user-events", @event.UserId);
            
            // Custom action
            outbox.AddCustomAction(async ct =>
            {
                // Custom async logic
                await SomeExternalService.NotifyAsync(@event.UserId, ct);
            }, "External notification");
        }

        return ValueTask.FromResult(updatedProjection);
    }
}
```

### Stream Configuration

```csharp
protected override void ConfigureEventStreams(EventStreamBuilder<UserProjection, UserEvent, UserOutboxEvent> builder)
{
    builder
        .ForEvent<UserCreated>()
        .InStream("user-lifecycle")
        .WithDeduplication(e => e.UserId)  // Type-safe access to event properties
        .HandledBy<UserCreatedHandler>();

    builder
        .ForEvent<UserEmailChanged>()
        .InStream("user-updates")
        .HandledBy(async (evt, projection, outbox) =>
        {
            // Inline handler with full outbox support
            var updated = projection with { Email = evt.NewEmail };
            
            outbox?.SendToStream(evt, "email-changes", evt.UserId);
            outbox?.AddCustomAction(async ct => await ValidateEmailAsync(evt.NewEmail, ct));
            
            return updated;
        });
}
```

### Dependency Injection Setup

```csharp
public static IServiceCollection AddEventSourcing(this IServiceCollection services)
{
    // Register with keyed services
    services.AddKeyedSingleton<IEventStorage<UserEvent, UserOutboxEvent>>("user-events", 
        (provider, key) => new AzureTableEventStorage<UserEvent, UserOutboxEvent>());

    // Register handlers
    services.AddScoped<UserCreatedHandler>();
    services.AddScoped<UserEmailChangedHandler>();

    return services;
}
```

## Migration Guide

### From EventStreamConfiguration

1. Update stream configuration types:
   ```csharp
   // Before
   EventStreamConfiguration config = new() { ... };
   
   // After  
   EventStreamConfiguration<UserEvent> config = new() { ... };
   ```

2. Update method signatures in derived classes:
   ```csharp
   // Before
   protected override void ConfigureEventStreams(EventStreamBuilder<UserProjection> builder)
   
   // After
   protected override void ConfigureEventStreams(EventStreamBuilder<UserProjection, UserEvent, UserOutboxEvent> builder)
   ```

### From EventOutbox

1. Update outbox usage:
   ```csharp
   // Before
   EventOutbox outbox = new();
   outbox.Add(someEvent, grainId);
   
   // After
   EventOutbox<UserOutboxEvent> outbox = new();
   outbox.Add(someEvent, grainId);
   outbox.SendToStream(someEvent, "stream-namespace", "stream-key");
   outbox.AddCustomAction(async ct => await DoSomethingAsync(ct));
   ```

### From Multiple Handler Interfaces

1. Consolidate handler implementations:
   ```csharp
   // Before
   public class Handler : IEventHandlerReadOnly<Event, Projection>
   {
       public ValueTask<Projection> ApplyEventAsync(Event evt, Projection proj)
       { ... }
   }
   
   // After  
   public class Handler : IEventHandler<Event, Projection, OutboxEvent>
   {
       public ValueTask<Projection> ApplyEventAsync(Event evt, Projection proj, EventOutbox<OutboxEvent>? outbox = null)
       { ... } // outbox will be null for read-only scenarios
   }
   ```

### From [EventStorage] to [FromKeyedServices]

1. Update constructor parameters:
   ```csharp
   // Before
   public UserGrain([EventStorage("key")] IEventStorage<A, B> storage)
   
   // After
   public UserGrain([FromKeyedServices("key")] IEventStorage<A, B> storage)
   ```

2. Add using statement:
   ```csharp
   using Microsoft.Extensions.DependencyInjection;
   ```

## Breaking Changes

1. **EventStreamConfiguration** is now generic - requires type parameter
2. **EventOutbox** is now generic - requires type parameter  
3. **EventStreamBuilder** now requires three type parameters instead of one
4. **IEventHandler** interfaces consolidated into single generic interface
5. **EventHandlerDelegate** signatures changed to include outbox parameter
6. **[EventStorage]** attribute replaced with **[FromKeyedServices]**
7. **Abstract base classes** for handlers removed
8. **ConfigureEventStreams** method signature updated with additional type parameters

## Benefits Summary

- **Type Safety**: Generic types eliminate casting and provide compile-time checking
- **Orleans Integration**: Native support for Orleans Streams in outbox pattern
- **Simplified API**: Single handler interface replaces multiple interfaces
- **Standard DI**: Uses Microsoft's keyed services instead of custom attributes
- **Extensibility**: Custom actions in outbox allow for flexible side effect handling
- **Performance**: Reduced object allocation and better type specialization
- **Maintainability**: Cleaner, more focused API surface
